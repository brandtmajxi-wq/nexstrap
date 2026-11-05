using System.Net.Http;
using Microsoft.Extensions.Logging;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

public interface ILastServerService
{
    /// <summary>The server to go back to, or null when there's none, it's too old, or you're still in it.</summary>
    LastServerInfo? Available { get; }

    Task<LaunchResult?> RejoinAsync();

    /// <summary>May be raised on a background thread.</summary>
    event EventHandler? Changed;
}

/// <summary>Remembers the last server from Roblox's log, so it can be joined again with Roblox's own server link.</summary>
public sealed class LastServerService : ILastServerService
{
    /// <summary>Public servers rarely last much longer, and an old entry would only lead to a failed join.</summary>
    internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly IRobloxSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ILaunchCoordinator _launch;
    private readonly IExperienceInfoService _experiences;
    private readonly ILogger<LastServerService> _logger;
    private bool _inServer;

    public LastServerService(IRobloxSessionService session, ISettingsService settings, ILaunchCoordinator launch,
        IExperienceInfoService experiences, ILogger<LastServerService> logger)
    {
        _session = session;
        _settings = settings;
        _launch = launch;
        _experiences = experiences;
        _logger = logger;
        session.Changed += (_, _) => Observe(_session.Current, DateTime.UtcNow);
    }

    public event EventHandler? Changed;

    private LauncherSettings Options => _settings.Current.Launcher;

    public LastServerInfo? Available => Pick(Options.LastServer, _session.Current, DateTime.UtcNow);

    internal static LastServerInfo? Pick(LastServerInfo? server, RobloxActivity? current, DateTime nowUtc)
    {
        if (server is null || nowUtc - server.SeenUtc > MaxAge) return null;
        return current is { Kind: RobloxActivityKind.InGame } && current.JobId == server.JobId ? null : server;
    }

    public Task<LaunchResult?> RejoinAsync() =>
        Available is { } server ? _launch.JoinAsync(server.PlaceId, server.Name, server.JobId) : Task.FromResult<LaunchResult?>(null);

    internal void Observe(RobloxActivity? activity, DateTime nowUtc)
    {
        var last = Options.LastServer;

        if (activity is { Kind: RobloxActivityKind.InGame, PlaceId: { } place, JobId: { } job } && Guid.TryParse(job, out _))
        {
            _inServer = true;
            if (last?.JobId == job)
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var known = Options.RecentExperiences.FirstOrDefault(r => r.PlaceId == place)?.Name;
            Options.LastServer = last = new LastServerInfo { PlaceId = place, JobId = job, Name = known ?? "", SeenUtc = nowUtc };
            _settings.Save();
            Changed?.Invoke(this, EventArgs.Empty);
            if (known is null) _ = FillNameAsync(last);
            return;
        }

        // Left the game: the 24 hours count from here.
        if (last is not null && _inServer)
        {
            _inServer = false;
            last.SeenUtc = nowUtc;
            _settings.Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task FillNameAsync(LastServerInfo server)
    {
        try
        {
            if (await _experiences.LookupAsync(server.PlaceId) is not { } info) return;
            server.Name = info.Name;
            _settings.Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Couldn't look up the name of place {Place}", server.PlaceId);
        }
    }
}
