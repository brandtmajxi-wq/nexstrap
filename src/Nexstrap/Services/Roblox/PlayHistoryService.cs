using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

/// <summary>Running totals for one experience. Totals are kept here so trimming old sessions doesn't lose them.</summary>
public sealed class GamePlayStats
{
    public long PlaceId { get; set; }
    public string Name { get; set; } = "";
    public double Seconds { get; set; }
    public int Sessions { get; set; }
    public DateTime LastPlayedUtc { get; set; }
}

public sealed class PlaySession
{
    public long PlaceId { get; set; }
    public DateTime StartedUtc { get; set; }
    public double Seconds { get; set; }
}

public sealed class PlayHistoryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<GamePlayStats> Games { get; set; } = [];
    public List<PlaySession> Sessions { get; set; } = [];
}

public interface IPlayHistoryService
{
    /// <summary>True when the setting is on, so Nexstrap should stay open during a session to count it.</summary>
    bool IsRecording { get; }

    /// <summary>Most played first.</summary>
    IReadOnlyList<GamePlayStats> Games { get; }

    double TotalSeconds { get; }

    int TotalSessions { get; }

    double SecondsSince(DateTime utc);

    double SecondsFor(long placeId);

    void Clear();

    /// <summary>Saves a game still in progress, for when Nexstrap closes before Roblox does.</summary>
    void Flush();

    /// <summary>May be raised on a background thread.</summary>
    event EventHandler? Changed;
}

/// <summary>Counts time spent in games, using the session Nexstrap already follows. Stored only in History.json on this PC.</summary>
public sealed class PlayHistoryService : IPlayHistoryService
{
    internal const int MaxSessions = 500;
    internal static readonly TimeSpan MinimumLength = TimeSpan.FromSeconds(15);

    private sealed record OpenGame(long PlaceId, string? JobId, DateTime StartedUtc);

    private readonly IExperienceInfoService _experiences;
    private readonly ISettingsService _settings;
    private readonly ILogger<PlayHistoryService> _logger;
    private readonly JsonFileStore<PlayHistoryDocument> _store;
    private readonly PlayHistoryDocument _document;
    private readonly object _gate = new();
    private readonly IRobloxSessionService _session;
    private OpenGame? _open;

    public PlayHistoryService(IRobloxSessionService session, IExperienceInfoService experiences, ISettingsService settings,
        ILogger<PlayHistoryService> logger)
        : this(session, experiences, settings, logger, Path.Combine(AppInfo.Paths.Root, "History.json")) { }

    internal PlayHistoryService(IRobloxSessionService session, IExperienceInfoService experiences, ISettingsService settings,
        ILogger<PlayHistoryService> logger, string path)
    {
        _session = session;
        _experiences = experiences;
        _settings = settings;
        _logger = logger;
        _store = new JsonFileStore<PlayHistoryDocument>(path, logger);
        _document = _store.Load();
        session.Changed += (_, _) => Observe(_session.Current, DateTime.UtcNow);
    }

    public event EventHandler? Changed;

    public bool IsRecording => _settings.Current.Launcher.RecordPlayHistory;

    public IReadOnlyList<GamePlayStats> Games
    {
        get { lock (_gate) return _document.Games.OrderByDescending(g => g.Seconds).ToList(); }
    }

    public double TotalSeconds
    {
        get { lock (_gate) return _document.Games.Sum(g => g.Seconds); }
    }

    public int TotalSessions
    {
        get { lock (_gate) return _document.Games.Sum(g => g.Sessions); }
    }

    public double SecondsSince(DateTime utc)
    {
        lock (_gate) return _document.Sessions.Where(s => s.StartedUtc >= utc).Sum(s => s.Seconds);
    }

    public double SecondsFor(long placeId)
    {
        lock (_gate) return _document.Games.FirstOrDefault(g => g.PlaceId == placeId)?.Seconds ?? 0;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _document.Games.Clear();
            _document.Sessions.Clear();
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Flush() => Observe(null, DateTime.UtcNow);

    /// <summary>A game ends when Roblox leaves it, closes, or moves to another place or server.</summary>
    internal void Observe(RobloxActivity? activity, DateTime nowUtc)
    {
        OpenGame? finished = null;
        lock (_gate)
        {
            var inGame = activity is { Kind: RobloxActivityKind.InGame, PlaceId: not null };
            if (_open is { } open && (!inGame || open.PlaceId != activity!.PlaceId || open.JobId != activity.JobId))
            {
                finished = open;
                _open = null;
            }
            if (inGame && _open is null && IsRecording)
                _open = new OpenGame(activity!.PlaceId!.Value, activity.JobId, activity.SinceUtc);
        }
        if (finished is not null) Record(finished, nowUtc);
    }

    private void Record(OpenGame game, DateTime endedUtc)
    {
        var length = endedUtc - game.StartedUtc;
        if (length < MinimumLength) return;

        GamePlayStats stats;
        lock (_gate)
        {
            stats = _document.Games.FirstOrDefault(g => g.PlaceId == game.PlaceId) ?? AddGame(game.PlaceId);
            stats.Seconds += length.TotalSeconds;
            stats.Sessions++;
            stats.LastPlayedUtc = endedUtc;

            _document.Sessions.Add(new PlaySession { PlaceId = game.PlaceId, StartedUtc = game.StartedUtc, Seconds = length.TotalSeconds });
            if (_document.Sessions.Count > MaxSessions) _document.Sessions.RemoveRange(0, _document.Sessions.Count - MaxSessions);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        if (stats.Name.Length == 0) _ = FillNameAsync(stats);
    }

    private GamePlayStats AddGame(long placeId)
    {
        var known = _settings.Current.Launcher.RecentExperiences.FirstOrDefault(r => r.PlaceId == placeId)?.Name;
        var stats = new GamePlayStats { PlaceId = placeId, Name = known ?? "" };
        _document.Games.Add(stats);
        return stats;
    }

    private async Task FillNameAsync(GamePlayStats stats)
    {
        try
        {
            if (await _experiences.LookupAsync(stats.PlaceId) is not { } info) return;
            lock (_gate)
            {
                stats.Name = info.Name;
                Persist();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Couldn't look up the name of place {Place}", stats.PlaceId);
        }
    }

    private void Persist()
    {
        try { _store.Save(_document); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save play history");
        }
    }

    public static string FormatDuration(double seconds)
    {
        var minutes = (long)(seconds / 60);
        if (minutes < 1) return Loc.Get("Format.UnderMinute");
        return minutes < 60 ? Loc.Format("Format.Minutes", minutes) : Loc.Format("Format.HoursMinutes", minutes / 60, minutes % 60);
    }
}
