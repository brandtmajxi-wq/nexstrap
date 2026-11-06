using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

public interface IRobloxAutoUpdater : IDisposable
{
    /// <summary>Starts watching for new Roblox versions.</summary>
    void Start();
}

/// <summary>
/// Installs new Roblox versions in the background while Roblox is closed, using the same verified
/// install as the Update button. It stops if Roblox starts, so it never takes bandwidth from a game.
/// </summary>
public sealed class RobloxAutoUpdater : IRobloxAutoUpdater
{
    private const string InstallLock = @"Local\Nexstrap.RobloxInstall";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(30);

    private readonly IRobloxClientService _client;
    private readonly IRobloxSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ILogger<RobloxAutoUpdater> _logger;
    private readonly Func<bool> _robloxRunning;
    private readonly string? _lockName;

    private DispatcherTimer? _timer;
    private CancellationTokenSource? _install;
    private string? _failedVersion;
    private bool _busy;

    public RobloxAutoUpdater(IRobloxClientService client, IRobloxLauncher launcher, IRobloxSessionService session,
        ISettingsService settings, ILogger<RobloxAutoUpdater> logger)
        : this(client, session, settings, logger, launcher.IsRobloxRunning, InstallLock) { }

    internal RobloxAutoUpdater(IRobloxClientService client, IRobloxSessionService session, ISettingsService settings,
        ILogger<RobloxAutoUpdater> logger, Func<bool> robloxRunning, string? lockName)
    {
        _client = client;
        _session = session;
        _settings = settings;
        _logger = logger;
        _robloxRunning = robloxRunning;
        _lockName = lockName;
    }

    /// <summary>
    /// Only people already using a Nexstrap build get this, so nobody gets a surprise download.
    /// A build pinned with Use for Play is left alone, and a version that failed isn't retried until the next start.
    /// </summary>
    internal static bool ShouldUpdate(ClientState state, LauncherSettings launcher, bool robloxRunning, string? failedVersion) =>
        launcher.AutoUpdateRoblox
        && !robloxRunning
        && launcher.PreferredInstallation is null
        && state is { UpdateAvailable: true, Latest: { } latest }
        && latest.VersionHash != failedVersion
        && state.Installations.Any(i => i.Source == InstallationSource.Nexstrap);

    public void Start()
    {
        if (_timer is not null) return;

        _client.StateChanged += (_, _) => Post(() => _ = CheckAsync());
        _session.Changed += (_, _) => Post(OnSessionChanged);
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += async (_, _) => await _client.RefreshAsync();
        _timer.Start();
    }

    private void OnSessionChanged()
    {
        if (_session.IsActive) _install?.Cancel();
        else _ = CheckAsync();
    }

    private static void Post(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    internal async Task CheckAsync()
    {
        if (_busy || !ShouldUpdate(_client.State, _settings.Current.Launcher, _robloxRunning(), _failedVersion)) return;

        // The installed copy and the dev copy share one folder of Roblox builds.
        using var shared = _lockName is null ? null : new Semaphore(1, 1, _lockName);
        if (shared?.WaitOne(0) == false) return;

        _busy = true;
        _install = new CancellationTokenSource();
        var version = _client.State.Latest!.VersionHash;
        try
        {
            _logger.LogInformation("Updating Roblox in the background to {Version}", version);
            await _client.InstallLatestAsync(null, _install.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Paused the Roblox update because Roblox started");
        }
        catch (Exception ex) when (ex is InstallException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            _failedVersion = version;
            _logger.LogWarning(ex, "Background Roblox update failed");
        }
        finally
        {
            _install.Dispose();
            _install = null;
            _busy = false;
            shared?.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _install?.Cancel();
    }
}
