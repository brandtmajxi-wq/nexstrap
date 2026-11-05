using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Shell;

namespace Nexstrap.Services.Notifications;

public interface IGameJoinAlerts;

/// <summary>Shows a Windows pop-up when a game finishes loading, unless you're already looking at Roblox.</summary>
public sealed class GameJoinAlerts : IGameJoinAlerts
{
    private readonly ISettingsService _settings;
    private readonly IExperienceInfoService _experiences;
    private readonly ITrayService _tray;
    private readonly IServerRegionService _regions;
    private readonly ILogger<GameJoinAlerts> _logger;
    private readonly Func<bool> _robloxInFront;
    private string? _alertedServer;

    public GameJoinAlerts(IRobloxSessionService session, ISettingsService settings, IExperienceInfoService experiences,
        ITrayService tray, IServerRegionService regions, ILogger<GameJoinAlerts> logger)
        : this(session, settings, experiences, tray, regions, logger, RobloxIsInFront) { }

    internal GameJoinAlerts(IRobloxSessionService session, ISettingsService settings, IExperienceInfoService experiences,
        ITrayService tray, IServerRegionService regions, ILogger<GameJoinAlerts> logger, Func<bool> robloxInFront)
    {
        _settings = settings;
        _experiences = experiences;
        _tray = tray;
        _regions = regions;
        _logger = logger;
        _robloxInFront = robloxInFront;
        session.Changed += (_, _) => _ = ObserveAsync(session.Current);
    }

    /// <summary>Roblox reports the universe in its "game loaded" log line, so that's the moment to alert.</summary>
    internal async Task ObserveAsync(RobloxActivity? activity)
    {
        if (activity is not { Kind: RobloxActivityKind.InGame })
        {
            _alertedServer = null;
            return;
        }

        if (activity is not { UniverseId: { } universe, PlaceId: { } place, JobId: { } job } || job == _alertedServer) return;
        _alertedServer = job;

        // A region search joins several servers, and it reports its own result.
        if (!_settings.Current.Notifications.GameJoinAlerts || _regions.IsSearching || _robloxInFront()) return;

        var region = RobloxRegions.Find(activity.ServerAddress);
        _tray.ShowAlert(Loc.Format("Alerts.GameReady", await NameAsync(universe, place)),
            region is null ? Loc.Get("Alerts.InGame") : Loc.Format("Servers.CurrentRegion", region));
    }

    private async Task<string> NameAsync(long universe, long place)
    {
        try
        {
            if (await _experiences.LookupUniverseAsync(universe, place) is { } info) return info.Name;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Couldn't look up the game for an alert");
        }
        return "Roblox";
    }

    private static bool RobloxIsInFront()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var id);
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName == "RobloxPlayerBeta";
        }
        catch (ArgumentException) { return false; }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
