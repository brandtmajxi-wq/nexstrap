using System.Windows.Threading;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;
using Nexstrap.Views;

namespace Nexstrap.Services.Notifications;

public interface IPlayReminder
{
    /// <summary>Shows the reminder right now, to see what it looks like.</summary>
    void Preview();
}

/// <summary>
/// A small note over Roblox each time the chosen amount of play time has passed. It's its own window because
/// Windows can hide tray pop-ups during a game, which is the one moment this has to be seen.
/// </summary>
public sealed class PlayReminder : IPlayReminder
{
    internal const int PreviewMinutes = 60;

    public static IReadOnlyList<int> Choices { get; } = [0, 30, 60, 90, 120, 180];

    private readonly IRobloxSessionService _session;
    private readonly ISettingsService _settings;
    private readonly Func<bool> _searching;
    private readonly Action<string, string> _show;
    private readonly DispatcherTimer? _timer;
    private DateTime? _counted;
    private int _shown;

    public PlayReminder(IRobloxSessionService session, ISettingsService settings, IServerRegionService regions)
        : this(session, settings, () => regions.IsSearching, ReminderToast.Pop)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Check(DateTime.UtcNow);
        _timer.Start();
    }

    internal PlayReminder(IRobloxSessionService session, ISettingsService settings, Func<bool> searching, Action<string, string> show)
    {
        _session = session;
        _settings = settings;
        _searching = searching;
        _show = show;
    }

    /// <summary>How many reminders should have appeared by now, if that's more than have, otherwise null.</summary>
    internal static int? Due(TimeSpan played, int everyMinutes, int shown)
    {
        if (everyMinutes <= 0) return null;
        var count = (int)(played.TotalMinutes / everyMinutes);
        return count > shown ? count : null;
    }

    public static string Label(int minutes)
    {
        if (minutes <= 0) return Loc.Get("Notifications.PlayReminder.Off");
        return minutes % 60 == 0 ? Loc.Format("Format.Hours", minutes / 60) : PlayHistoryService.FormatDuration(minutes * 60d);
    }

    public void Preview()
    {
        var minutes = _settings.Current.Notifications.PlayReminderMinutes;
        Show(minutes > 0 ? minutes : PreviewMinutes);
    }

    internal void Check(DateTime nowUtc)
    {
        if (!_session.IsActive || _session.StartedUtc is not { } started)
        {
            _counted = null;
            return;
        }

        if (started != _counted)
        {
            _counted = started;
            _shown = 0;
        }

        var every = _settings.Current.Notifications.PlayReminderMinutes;
        if (_searching() || Due(nowUtc - started, every, _shown) is not { } count) return;

        // After sleep several can be due at once, and one is enough.
        _shown = count;
        Show(count * every);
    }

    private void Show(int minutes) => _show(Loc.Format("Reminder.Title", Label(minutes)), Loc.Get("Reminder.Text"));
}
