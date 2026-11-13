using System.Globalization;
using System.Windows.Threading;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;
using Nexstrap.Views;

namespace Nexstrap.Services.Notifications;

public interface IDailyLimit
{
    /// <summary>Shows the note right now, to see what it looks like.</summary>
    void Preview();
}

/// <summary>
/// A note over Roblox once today's play time reaches the limit you chose. Today counts every saved game plus
/// the one in progress, and the note appears once a day, also across restarts of Nexstrap.
/// </summary>
public sealed class DailyLimit : IDailyLimit
{
    public static IReadOnlyList<int> Choices { get; } = [0, 60, 120, 180, 240, 360];

    private readonly IRobloxSessionService _session;
    private readonly IPlayHistoryService _history;
    private readonly ISettingsService _settings;
    private readonly Func<bool> _searching;

    public DailyLimit(IRobloxSessionService session, IPlayHistoryService history, ISettingsService settings, IServerRegionService regions)
    {
        _session = session;
        _history = history;
        _settings = settings;
        _searching = () => regions.IsSearching;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => Check();
        timer.Start();
    }

    /// <summary>True when today's play has reached the limit and the note hasn't been shown today.</summary>
    internal static bool Due(double todaySeconds, int limitMinutes, string? shownOn, DateOnly today) =>
        limitMinutes > 0 && todaySeconds >= limitMinutes * 60 && shownOn != Day(today);

    internal static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void Preview()
    {
        var limit = _settings.Current.Notifications.DailyLimitMinutes;
        Show(limit > 0 ? limit : 120);
    }

    private void Check()
    {
        var notes = _settings.Current.Notifications;
        if (notes.DailyLimitMinutes <= 0 || !_session.IsActive || _searching()) return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!Due(TodaySeconds(), notes.DailyLimitMinutes, notes.DailyLimitShownOn, today)) return;

        notes.DailyLimitShownOn = Day(today);
        _settings.Save();
        Show(notes.DailyLimitMinutes);
    }

    /// <summary>Saved games since midnight, plus the game being played now (the history only saves a game once it ends).</summary>
    private double TodaySeconds()
    {
        var midnight = DateTime.Today.ToUniversalTime();
        var now = DateTime.UtcNow;
        var saved = _history.IsRecording ? _history.SecondsSince(midnight) : 0;
        if (_session.Current is { Kind: RobloxActivityKind.InGame } game)
            return saved + (now - (game.SinceUtc > midnight ? game.SinceUtc : midnight)).TotalSeconds;
        return saved;
    }

    private static void Show(int minutes) =>
        ReminderToast.Pop(Loc.Format("Limit.Title", PlayReminder.Label(minutes)), Loc.Get("Limit.Text"));
}
