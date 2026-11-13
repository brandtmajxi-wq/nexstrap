using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Notifications;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Shell;
using Xunit;

namespace Nexstrap.Tests;

public class CrashDetectionTests
{
    // The closing lines of real Roblox logs.
    private static readonly string[] NormalClose =
    [
        "2026-11-06T16:42:21.1Z,1.0,1,6 [FLog::SingleSurfaceApp] shutDown: (stage:LuaApp).",
        "2026-11-06T16:42:21.5Z,1.0,1,6 [DFLog::AppPlatformQoSEmergency] AppPlatformQoSEmergencyHandler was destroyed.",
        "2026-11-06T16:42:21.5Z,1.0,1,6 [DFLog::SystemDialogHandler] Platform handler was destroyed."
    ];

    private static readonly string[] CutOff =
    [
        "2026-11-06T10:19:01.1Z,1.0,1,6 [FLog::DynamicFastVariableReloader] DynamicFastVariableReloader finished flag fetch.",
        "2026-11-06T10:19:02.1Z,1.0,1,6 [FLog::SessionL2ValidationHelper] onSessionHeartbeat"
    ];

    [Fact]
    public void A_normal_close_is_never_a_crash_whatever_the_exit_code() =>
        Assert.False(Nexstrap.Services.Roblox.RobloxLogParser.IsCrash(1, NormalClose));

    [Fact]
    public void A_log_that_stops_with_a_bad_exit_code_is_a_crash() =>
        Assert.True(Nexstrap.Services.Roblox.RobloxLogParser.IsCrash(unchecked((int)0xC0000005), CutOff));

    [Fact]
    public void Exit_code_zero_is_trusted_even_if_the_log_format_changes() =>
        Assert.False(Nexstrap.Services.Roblox.RobloxLogParser.IsCrash(0, CutOff));

    [Fact]
    public void An_unknown_exit_code_falls_back_to_the_log()
    {
        Assert.True(Nexstrap.Services.Roblox.RobloxLogParser.IsCrash(null, CutOff));
        Assert.False(Nexstrap.Services.Roblox.RobloxLogParser.IsCrash(null, NormalClose));
    }
}

public class CrashAlertsTests
{
    private static readonly RobloxCrash InGame = new(123, 1, @"C:\logs\player.log");
    private static readonly RobloxCrash InMenu = new(null, 1, @"C:\logs\player.log");

    private sealed class FakeServer(LastServerInfo? server) : ILastServerService
    {
        public int Rejoins { get; private set; }
        public LastServerInfo? Available => server;
        public event EventHandler? Changed { add { } remove { } }
        public Task<LaunchResult?> RejoinAsync()
        {
            Rejoins++;
            return Task.FromResult<LaunchResult?>(null);
        }
    }

    private sealed class FakeDialogs(DialogChoice answer) : IDialogService
    {
        public string? Extra { get; private set; }
        public string? Confirm { get; private set; }
        public int Asked { get; private set; }
        public void Show(string title, string message, DialogKind kind = DialogKind.Info, string? details = null) { }
        bool IDialogService.Confirm(string title, string message, string confirmText, string cancelText, bool cancelIsDefault) => false;
        public string? Ask(string title, string message, string confirmText, string cancelText, int maxLength, Func<string, string?>? validate = null) => null;
        public DialogChoice Choose(string title, string message, string confirmText, string? extraText, string cancelText)
        {
            Asked++;
            Confirm = confirmText;
            Extra = extraText;
            return answer;
        }
    }

    private sealed class FakeShell : IShellService
    {
        public string? Opened { get; private set; }
        public void OpenUrl(string url) { }
        public void OpenFolder(string path) => Opened = path;
        public void CopyToClipboard(string text) { }
        public void ShowInFolder(string file) => Opened = file;
    }

    private static readonly LastServerInfo Server = new() { PlaceId = 123, JobId = "job", Name = "Game", SeenUtc = DateTime.UtcNow };

    private static CrashAlerts Create(FakeServer server, FakeDialogs dialogs, FakeShell shell, MemorySettings? settings = null, bool searching = false) =>
        new(new ManualSession(), settings ?? new MemorySettings(), server, dialogs, shell, NullLogger<CrashAlerts>.Instance, () => searching, work => work());

    [Fact]
    public void Rejoin_goes_back_to_the_server()
    {
        var server = new FakeServer(Server);

        Create(server, new FakeDialogs(DialogChoice.Confirm), new FakeShell()).Handle(InGame);

        Assert.Equal(1, server.Rejoins);
    }

    [Fact]
    public void The_third_button_opens_the_logs_folder()
    {
        var shell = new FakeShell();
        var server = new FakeServer(Server);

        Create(server, new FakeDialogs(DialogChoice.Extra), shell).Handle(InGame);

        Assert.Equal(@"C:\logs", shell.Opened);
        Assert.Equal(0, server.Rejoins);
    }

    [Fact]
    public void Closing_the_question_does_nothing()
    {
        var shell = new FakeShell();
        var server = new FakeServer(Server);

        Create(server, new FakeDialogs(DialogChoice.Cancel), shell).Handle(InGame);

        Assert.Null(shell.Opened);
        Assert.Equal(0, server.Rejoins);
    }

    [Fact]
    public void A_crash_in_the_menu_doesnt_offer_an_old_server()
    {
        var dialogs = new FakeDialogs(DialogChoice.Confirm);
        var server = new FakeServer(Server);
        var shell = new FakeShell();

        Create(server, dialogs, shell).Handle(InMenu);

        Assert.Null(dialogs.Extra);
        Assert.Equal(0, server.Rejoins);
        Assert.Equal(@"C:\logs", shell.Opened);
    }

    [Fact]
    public void A_different_game_isnt_offered()
    {
        var dialogs = new FakeDialogs(DialogChoice.Confirm);
        var server = new FakeServer(new LastServerInfo { PlaceId = 999, JobId = "job", SeenUtc = DateTime.UtcNow });

        Create(server, dialogs, new FakeShell()).Handle(InGame);

        Assert.Equal(0, server.Rejoins);
    }

    [Fact]
    public void Respects_the_setting()
    {
        var settings = new MemorySettings();
        settings.Current.Notifications.CrashAlerts = false;
        var dialogs = new FakeDialogs(DialogChoice.Confirm);

        Create(new FakeServer(Server), dialogs, new FakeShell(), settings).Handle(InGame);

        Assert.Equal(0, dialogs.Asked);
    }

    [Fact]
    public void A_region_search_closing_roblox_isnt_a_crash()
    {
        var dialogs = new FakeDialogs(DialogChoice.Confirm);

        Create(new FakeServer(Server), dialogs, new FakeShell(), searching: true).Handle(InGame);

        Assert.Equal(0, dialogs.Asked);
    }
}

public class PlayReminderTests
{
    private static readonly DateTime Start = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static (PlayReminder Reminder, ManualSession Session, List<string> Shown) Create(int minutes, bool searching = false)
    {
        var settings = new MemorySettings();
        settings.Current.Notifications.PlayReminderMinutes = minutes;
        var session = new ManualSession();
        var shown = new List<string>();
        return (new PlayReminder(session, settings, () => searching, (title, _) => shown.Add(title)), session, shown);
    }

    [Theory]
    [InlineData(59, 60, 0, null)]
    [InlineData(60, 60, 0, 1)]
    [InlineData(61, 60, 1, null)]
    [InlineData(120, 60, 1, 2)]
    [InlineData(500, 60, 1, 8)]
    [InlineData(500, 0, 0, null)]
    public void Due(int played, int every, int shown, int? expected) =>
        Assert.Equal(expected, PlayReminder.Due(TimeSpan.FromMinutes(played), every, shown));

    [Fact]
    public void Shows_once_per_interval()
    {
        var (reminder, session, shown) = Create(60);
        session.Set(new RobloxActivity(RobloxActivityKind.InGame, Start));

        reminder.Check(Start.AddMinutes(59));
        Assert.Empty(shown);

        reminder.Check(Start.AddMinutes(60));
        reminder.Check(Start.AddMinutes(61));
        Assert.Single(shown);

        reminder.Check(Start.AddMinutes(120));
        Assert.Equal(2, shown.Count);
    }

    [Fact]
    public void Waking_from_sleep_shows_one_not_many()
    {
        var (reminder, session, shown) = Create(30);
        session.Set(new RobloxActivity(RobloxActivityKind.InGame, Start));

        reminder.Check(Start.AddHours(5));
        reminder.Check(Start.AddHours(5).AddMinutes(1));

        Assert.Single(shown);
    }

    [Fact]
    public void A_new_session_starts_counting_again()
    {
        var (reminder, session, shown) = Create(60);
        session.Set(new RobloxActivity(RobloxActivityKind.InGame, Start));
        reminder.Check(Start.AddMinutes(60));

        session.Set(null);
        reminder.Check(Start.AddMinutes(61));
        var next = Start.AddHours(3);
        session.Set(new RobloxActivity(RobloxActivityKind.InGame, next));
        reminder.Check(next.AddMinutes(30));
        Assert.Single(shown);

        reminder.Check(next.AddMinutes(60));
        Assert.Equal(2, shown.Count);
    }

    [Fact]
    public void Off_and_no_session_show_nothing()
    {
        var (off, session, shown) = Create(0);
        session.Set(new RobloxActivity(RobloxActivityKind.InGame, Start));
        off.Check(Start.AddHours(9));
        Assert.Empty(shown);

        var (idle, _, idleShown) = Create(30);
        idle.Check(Start.AddHours(9));
        Assert.Empty(idleShown);
    }

    [Fact]
    public void Waits_during_a_region_search()
    {
        var (reminder, session, shown) = Create(30, searching: true);
        session.Set(new RobloxActivity(RobloxActivityKind.InGame, Start));

        reminder.Check(Start.AddHours(1));

        Assert.Empty(shown);
    }

    [Fact]
    public void Preview_works_even_while_off()
    {
        var (reminder, _, shown) = Create(0);

        reminder.Preview();

        Assert.Single(shown);
    }
}
