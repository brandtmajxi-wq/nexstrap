using System.IO;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Services.Notifications;

public sealed class CrashRecord
{
    public DateTime TimeUtc { get; set; }
    public long? PlaceId { get; set; }
    public string Name { get; set; } = "";
    public int? ExitCode { get; set; }
    public string LogPath { get; set; } = "";
}

public sealed class CrashHistoryDocument
{
    public List<CrashRecord> Crashes { get; set; } = [];
}

public interface ICrashHistory
{
    /// <summary>Newest first.</summary>
    IReadOnlyList<CrashRecord> Recent { get; }

    void Clear();

    /// <summary>May be raised on a background thread.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// Keeps the last 20 times Roblox stopped suddenly, with the game and its log, in Crashes.json on this PC.
/// It uses the same check as the crash alert, and records crashes even when the alert is off.
/// </summary>
public sealed class CrashHistory : ICrashHistory
{
    internal const int Keep = 20;

    private readonly ISettingsService _settings;
    private readonly IPlayHistoryService _history;
    private readonly Func<bool> _searching;
    private readonly JsonFileStore<CrashHistoryDocument> _store;
    private readonly CrashHistoryDocument _document;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    public CrashHistory(IRobloxSessionService session, ISettingsService settings, IPlayHistoryService history, IServerRegionService regions,
        ILogger<CrashHistory> logger)
        : this(session, settings, history, () => regions.IsSearching, logger, Path.Combine(AppInfo.Paths.Root, "Crashes.json")) { }

    internal CrashHistory(IRobloxSessionService session, ISettingsService settings, IPlayHistoryService history, Func<bool> searching,
        ILogger logger, string path)
    {
        _settings = settings;
        _history = history;
        _searching = searching;
        _logger = logger;
        _store = new JsonFileStore<CrashHistoryDocument>(path, logger);
        _document = _store.Load();
        session.Crashed += (_, crash) => Add(crash, DateTime.UtcNow);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<CrashRecord> Recent
    {
        get { lock (_gate) return _document.Crashes.OrderByDescending(c => c.TimeUtc).ToList(); }
    }

    internal void Add(RobloxCrash crash, DateTime nowUtc)
    {
        // A region search closes Roblox on purpose.
        if (_searching()) return;

        lock (_gate)
        {
            _document.Crashes.Add(new CrashRecord
            {
                TimeUtc = nowUtc,
                PlaceId = crash.PlaceId,
                Name = crash.PlaceId is { } place ? NameOf(place) : "",
                ExitCode = crash.ExitCode,
                LogPath = crash.LogPath
            });
            var extra = _document.Crashes.Count - Keep;
            if (extra > 0) _document.Crashes = _document.Crashes.OrderByDescending(c => c.TimeUtc).Take(Keep).ToList();
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _document.Crashes.Clear();
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string NameOf(long place) =>
        _settings.Current.Launcher.RecentExperiences.FirstOrDefault(r => r.PlaceId == place)?.Name is { Length: > 0 } recent
            ? recent
            : _history.Games.FirstOrDefault(g => g.PlaceId == place)?.Name ?? "";

    private void Save()
    {
        try { _store.Save(_document); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the crash history");
        }
    }
}
