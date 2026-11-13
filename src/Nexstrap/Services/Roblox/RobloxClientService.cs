using Nexstrap.Core;
using System.IO;
using Microsoft.Extensions.Logging;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

/// <summary>Snapshot of everything Nexstrap knows about Roblox on this PC.</summary>
public sealed record ClientState(
    IReadOnlyList<RobloxInstallation> Installations,
    RobloxInstallation? Preferred,
    ProtocolHandlerInfo ProtocolHandler,
    ClientVersionInfo? Latest,
    string? LatestError)
{
    public static ClientState Empty { get; } = new([], null, new ProtocolHandlerInfo(null), null, null);

    /// <summary>The build Play would use differs from Roblox's current live build.</summary>
    public bool UpdateAvailable => Latest is not null && Preferred?.VersionHash != Latest.VersionHash;

    public bool IsUpToDate => Latest is not null && Preferred?.VersionHash == Latest.VersionHash;
}

public interface IRobloxClientService
{
    ClientState State { get; }

    event EventHandler? StateChanged;

    /// <summary>Rescans disk; with <paramref name="checkLatest"/> also asks Roblox for the live version.</summary>
    Task RefreshAsync(bool checkLatest = true, CancellationToken ct = default);

    /// <summary>Pin Play to one installation, or null for automatic.</summary>
    void SetPreferred(RobloxInstallation? installation);

    bool IsAutomaticSelection { get; }

    /// <summary>Installs Roblox's live build into Nexstrap's folder and removes older Nexstrap builds.</summary>
    Task<RobloxInstallation> InstallLatestAsync(IProgress<InstallProgress>? progress, CancellationToken ct = default);

    /// <summary>Deletes a Nexstrap-managed build. Builds from other launchers are never touched.</summary>
    void Remove(RobloxInstallation installation);

    /// <summary>Downloads a Nexstrap-managed build's version again with the usual checks and swaps it in once it's complete.</summary>
    Task<RobloxInstallation> RepairAsync(RobloxInstallation installation, IProgress<InstallProgress>? progress, CancellationToken ct = default);
}

public sealed class RobloxClientService(
    IRobloxLocator locator,
    IRobloxDeployment deployment,
    IRobloxLauncher launcher,
    ISettingsService settings,
    ILogger<RobloxClientService> logger) : IRobloxClientService
{
    private readonly SemaphoreSlim _installGate = new(1, 1);

    public ClientState State { get; private set; } = ClientState.Empty;

    public event EventHandler? StateChanged;

    public bool IsAutomaticSelection => settings.Current.Launcher.PreferredInstallation is null;

    public async Task RefreshAsync(bool checkLatest = true, CancellationToken ct = default)
    {
        var (installs, handler) = await Task.Run(() => (locator.FindInstallations(), locator.GetProtocolHandler()), ct);

        var latest = State.Latest;
        string? latestError = null;
        if (checkLatest)
        {
            try { latest = await deployment.GetLatestVersionAsync(ct); }
            catch (InstallException ex)
            {
                latestError = ex.Message;
                logger.LogWarning(ex, "Latest Roblox version unavailable");
            }
        }

        Publish(new ClientState(installs, ResolvePreferred(installs, latest), handler, latest, latestError));
    }

    /// <summary>
    /// Pinned choice if it still exists; otherwise a build matching Roblox's live version
    /// (Nexstrap's own first); otherwise Nexstrap's newest build; otherwise the most recently updated one.
    /// Roblox updates often, so for a while after each update no build matches and this last step decides.
    /// </summary>
    internal RobloxInstallation? ResolvePreferred(IReadOnlyList<RobloxInstallation> installs, ClientVersionInfo? latest)
    {
        var pinned = settings.Current.Launcher.PreferredInstallation;
        if (pinned is not null)
        {
            var match = installs.FirstOrDefault(i => string.Equals(i.Directory, pinned, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        if (latest is not null)
        {
            var current = installs.Where(i => i.VersionHash == latest.VersionHash)
                .OrderBy(i => i.Source == InstallationSource.Nexstrap ? 0 : 1)
                .FirstOrDefault();
            if (current is not null) return current;
        }

        return installs.FirstOrDefault(i => i.Source == InstallationSource.Nexstrap) ?? installs.FirstOrDefault();
    }

    public void SetPreferred(RobloxInstallation? installation)
    {
        settings.Current.Launcher.PreferredInstallation = installation?.Directory;
        settings.Save();
        Publish(State with { Preferred = ResolvePreferred(State.Installations, State.Latest) });
    }

    public async Task<RobloxInstallation> InstallLatestAsync(IProgress<InstallProgress>? progress, CancellationToken ct = default)
    {
        if (!await _installGate.WaitAsync(0, ct))
            throw new InstallException(Loc.Get("Launch.AnInstallationIsAlreadyRunning"));
        try
        {
            var latest = await deployment.GetLatestVersionAsync(ct);
            var dir = await deployment.InstallAsync(latest, progress, ct);

            // A pin to an outdated build would hide the update, so go back to automatic.
            settings.Current.Launcher.PreferredInstallation = null;
            settings.Save();

            await RefreshAsync(checkLatest: false, ct);
            Publish(State with { Latest = latest, LatestError = null, Preferred = ResolvePreferred(State.Installations, latest) });
            RemoveOutdatedNexstrapBuilds(latest.VersionHash);

            return State.Installations.First(i => string.Equals(i.Directory, dir, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _installGate.Release();
        }
    }

    public async Task<RobloxInstallation> RepairAsync(RobloxInstallation installation, IProgress<InstallProgress>? progress, CancellationToken ct = default)
    {
        if (installation.Source != InstallationSource.Nexstrap)
            throw new InvalidOperationException(Loc.Get("Launch.OnlyBuildsInstalledByNexstrapCan"));
        if (launcher.IsRobloxRunning())
            throw new InvalidOperationException(Loc.Get("Home.Repair.CloseRoblox"));
        if (!await _installGate.WaitAsync(0, ct))
            throw new InstallException(Loc.Get("Launch.AnInstallationIsAlreadyRunning"));
        try
        {
            var dir = await deployment.RepairAsync(new ClientVersionInfo(installation.ClientVersion ?? "", installation.VersionHash), progress, ct);
            await RefreshAsync(checkLatest: false, ct);
            return State.Installations.FirstOrDefault(i => string.Equals(i.Directory, dir, StringComparison.OrdinalIgnoreCase)) ?? installation;
        }
        finally
        {
            _installGate.Release();
        }
    }

    public void Remove(RobloxInstallation installation)
    {
        if (installation.Source != InstallationSource.Nexstrap)
            throw new InvalidOperationException(Loc.Get("Launch.OnlyBuildsInstalledByNexstrapCan"));
        if (launcher.IsRobloxRunning())
            throw new InvalidOperationException(Loc.Get("Launch.CloseRobloxBeforeRemovingAnInstallation"));

        Directory.Delete(installation.Directory, recursive: true);
        logger.LogInformation("Removed {Dir}", installation.Directory);

        if (string.Equals(settings.Current.Launcher.PreferredInstallation, installation.Directory, StringComparison.OrdinalIgnoreCase))
        {
            settings.Current.Launcher.PreferredInstallation = null;
            settings.Save();
        }

        var remaining = State.Installations.Where(i => i != installation).ToList();
        Publish(State with { Installations = remaining, Preferred = ResolvePreferred(remaining, State.Latest) });
    }

    private void RemoveOutdatedNexstrapBuilds(string keepHash)
    {
        if (launcher.IsRobloxRunning()) return; // files may be in use; try again next update

        foreach (var old in State.Installations.Where(i => i.Source == InstallationSource.Nexstrap && i.VersionHash != keepHash).ToList())
        {
            try { Remove(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Could not remove outdated build {Dir}", old.Directory);
            }
        }
    }

    private void Publish(ClientState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
