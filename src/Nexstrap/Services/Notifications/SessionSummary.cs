using System.Windows;
using System.Windows.Threading;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;
using Nexstrap.Views;

namespace Nexstrap.Services.Notifications;

public interface ISessionSummary
{
    /// <summary>Shows a summary of today right now, to see what it looks like.</summary>
    void Preview();
}

/// <summary>
/// When Roblox closes, a small note in the corner says how long you played and which games, plus today's total.
/// It waits a few seconds so the play history has saved the last game, and stays away after a crash,
/// which has its own alert, and during a region search, which closes Roblox between tries.
/// </summary>
public sealed class SessionSummary : ISessionSummary
{
    internal static readonly TimeSpan MinimumPlay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(4);

    private readonly IRobloxSessionService _session;
    private readonly IPlayHistoryService _history;
    private readonly ISettingsService _settings;
    private readonly Func<bool> _searching;
    private DateTime? _started;
    private volatile bool _crashed;

    public SessionSummary(IRobloxSessionService session, IPlayHistoryService history, ISettingsService settings, IServerRegionService regions)
    {
        _session = session;
        _history = history;
        _settings = settings;
        _searching = () => regions.IsSearching;
        session.Crashed += (_, _) => _crashed = true;
        session.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(OnChanged);
    }

    private void OnChanged()
    {
        if (_session.IsActive)
        {
            if (_started is null)
            {
                _started = _session.StartedUtc ?? DateTime.UtcNow;
                _crashed = false;
            }
            return;
        }

        if (_started is not { } started) return;
        _started = null;
        var timer = new DispatcherTimer { Interval = Delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_session.IsActive || _crashed || _searching() || !_settings.Current.Notifications.SessionSummary) return;
            if (Build(_history.Sessions, _history.Games, started, TodaySeconds()) is { } note) ReminderToast.Pop(note.Title, note.Text);
        };
        timer.Start();
    }

    public void Preview()
    {
        var today = TodaySeconds();
        var note = Build(_history.Sessions, _history.Games, DateTime.Today.ToUniversalTime(), today)
            ?? (Loc.Format("Summary.Title", PlayHistoryService.FormatDuration(42 * 60)), Loc.Format("Summary.OneGame", "Roblox", PlayHistoryService.FormatDuration(today + 42 * 60)));
        ReminderToast.Pop(note.Title, note.Text);
    }

    private double TodaySeconds() => _history.SecondsSince(DateTime.Today.ToUniversalTime());

    /// <summary>The note for games started since <paramref name="startedUtc"/>, or null if that was under two minutes of play.</summary>
    internal static (string Title, string Text)? Build(IReadOnlyList<PlaySession> sessions, IReadOnlyList<GamePlayStats> games,
        DateTime startedUtc, double todaySeconds)
    {
        var played = sessions.Where(s => s.StartedUtc >= startedUtc).ToList();
        var total = played.Sum(s => s.Seconds);
        if (total < MinimumPlay.TotalSeconds) return null;

        var names = games.ToDictionary(g => g.PlaceId, g => g.Name.Length > 0 ? g.Name : Loc.Format("Launch.Place", g.PlaceId));
        var byGame = played
            .GroupBy(s => names.TryGetValue(s.PlaceId, out var name) ? name : Loc.Format("Launch.Place", s.PlaceId))
            .OrderByDescending(g => g.Sum(s => s.Seconds))
            .Select(g => g.Key)
            .ToList();

        var today = PlayHistoryService.FormatDuration(Math.Max(todaySeconds, total));
        var text = byGame.Count == 1
            ? Loc.Format("Summary.OneGame", byGame[0], today)
            : Loc.Format("Summary.Games", byGame[0], byGame.Count - 1, today);
        return (Loc.Format("Summary.Title", PlayHistoryService.FormatDuration(total)), text);
    }
}
