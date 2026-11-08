using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;

namespace Nexstrap.Services.Roblox;

public sealed record StudioBuild(InstallationSource Source, string Directory, string Executable, string VersionHash, string? Version, DateTime LastModified)
{
    public string DisplayVersion => Version ?? VersionHash;
}

/// <summary>Read-only search for Roblox Studio builds. Other launchers' folders are only looked at, never changed.</summary>
public static class StudioLocator
{
    public const string StudioExecutable = "RobloxStudioBeta.exe";

    internal static IEnumerable<(InstallationSource Source, string Root)> SearchRoots() =>
        RobloxLocator.SearchRoots().Select(r => r.Source == InstallationSource.Nexstrap ? (r.Source, AppInfo.Paths.Studio) : r);

    internal static IReadOnlyList<StudioBuild> Scan(IEnumerable<(InstallationSource Source, string Root)> roots, ILogger? logger = null)
    {
        var found = new List<StudioBuild>();
        foreach (var (source, root) in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "version-*"))
                {
                    var exe = Path.Combine(dir, StudioExecutable);
                    if (!File.Exists(exe)) continue;
                    if (source == InstallationSource.Nexstrap && !File.Exists(Path.Combine(dir, RobloxDeployment.CompleteMarker))) continue;
                    found.Add(new StudioBuild(source, dir, exe, Path.GetFileName(dir), RobloxLocator.ReadClientVersion(exe), File.GetLastWriteTime(exe)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(ex, "Could not scan {Root} for Studio", root);
            }
        }
        return found.OrderByDescending(b => b.LastModified).ToList();
    }

    /// <summary>Nexstrap's own newest build if there is one, otherwise the newest build found.</summary>
    internal static StudioBuild? Pick(IReadOnlyList<StudioBuild> builds) =>
        builds.FirstOrDefault(b => b.Source == InstallationSource.Nexstrap) ?? builds.FirstOrDefault();
}

public interface IStudioService
{
    StudioBuild? Preferred { get; }

    /// <summary>Null until checked, or if Roblox couldn't be reached.</summary>
    ClientVersionInfo? Latest { get; }

    bool UpdateAvailable { get; }

    bool IsRunning { get; }

    Task RefreshAsync(CancellationToken ct = default);

    Task InstallLatestAsync(IProgress<InstallProgress>? progress, CancellationToken ct = default);

    LaunchResult Open();
}

/// <summary>
/// Finds, opens and installs Roblox Studio. Builds come unmodified from Roblox's own CDN with the same checks as the Player,
/// and Studio handles its own sign-in.
/// </summary>
public sealed class StudioService : IStudioService
{
    private const string ProcessName = "RobloxStudioBeta";

    private readonly IRobloxDeployment _deployment;
    private readonly ILogger<StudioService> _logger;
    private readonly Func<IReadOnlyList<StudioBuild>> _scan;
    private IReadOnlyList<StudioBuild> _builds = [];

    /// <param name="deployment">Made with RobloxDeployment.ForStudio.</param>
    public StudioService(IRobloxDeployment deployment, ILogger<StudioService> logger)
        : this(deployment, logger, () => StudioLocator.Scan(StudioLocator.SearchRoots(), logger)) { }

    internal StudioService(IRobloxDeployment deployment, ILogger<StudioService> logger, Func<IReadOnlyList<StudioBuild>> scan)
    {
        _deployment = deployment;
        _logger = logger;
        _scan = scan;
    }

    public StudioBuild? Preferred => StudioLocator.Pick(_builds);

    public ClientVersionInfo? Latest { get; private set; }

    public bool UpdateAvailable => Preferred is { } build && Latest is { } latest && build.VersionHash != latest.VersionHash;

    public bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var p in processes) p.Dispose();
            return processes.Length > 0;
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _builds = await Task.Run(_scan, ct);
        try
        {
            Latest = await _deployment.GetLatestVersionAsync(ct);
        }
        catch (InstallException ex)
        {
            _logger.LogInformation(ex, "Couldn't check the latest Studio version");
            Latest = null;
        }
    }

    public async Task InstallLatestAsync(IProgress<InstallProgress>? progress, CancellationToken ct = default)
    {
        var latest = await _deployment.GetLatestVersionAsync(ct);
        Latest = latest;
        var dir = await _deployment.InstallAsync(latest, progress, ct);
        _builds = await Task.Run(_scan, ct);
        RemoveOldBuilds(dir);
    }

    /// <summary>Only Nexstrap's own older Studio builds, and only while Studio is closed.</summary>
    private void RemoveOldBuilds(string keep)
    {
        if (IsRunning) return;
        foreach (var old in _builds.Where(b => b.Source == InstallationSource.Nexstrap
                                               && !string.Equals(b.Directory, keep, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            try
            {
                Directory.Delete(old.Directory, recursive: true);
                _logger.LogInformation("Removed old Studio build {Dir}", old.Directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Couldn't remove old Studio build {Dir}", old.Directory);
            }
        }
        _builds = _scan();
    }

    public LaunchResult Open()
    {
        if (Preferred is not { } build || !File.Exists(build.Executable))
            return new LaunchResult(false, Loc.Get("Studio.NotFound"));
        try
        {
            var process = Process.Start(new ProcessStartInfo(build.Executable) { WorkingDirectory = build.Directory, UseShellExecute = false });
            if (process is null) return new LaunchResult(false, Loc.Get("Studio.DidntStart"));
            _logger.LogInformation("Started Roblox Studio {Version} from {Dir} (pid {Pid})", build.DisplayVersion, build.Directory, process.Id);
            var id = process.Id;
            process.Dispose();
            return new LaunchResult(true, "", id);
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Failed to start {Exe}", build.Executable);
            return new LaunchResult(false, Loc.Format("Studio.WindowsCouldntStart", ex.Message));
        }
    }
}
