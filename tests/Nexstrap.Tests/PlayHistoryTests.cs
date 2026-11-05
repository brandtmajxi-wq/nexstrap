using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Setup;
using Xunit;

namespace Nexstrap.Tests;

public sealed class PlayHistoryTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("nexstrap-history-").FullName;
    private readonly ManualSession _session = new();
    private readonly MemorySettings _settings = new();
    private readonly NamedExperiences _experiences = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class NamedExperiences : IExperienceInfoService
    {
        public Task<ExperienceInfo?> LookupAsync(long placeId, CancellationToken ct = default) =>
            Task.FromResult<ExperienceInfo?>(new ExperienceInfo(placeId, 1, "Looked up game", "Someone", 0));
        public Task<ExperienceInfo?> LookupUniverseAsync(long universeId, long placeId, CancellationToken ct = default) => LookupAsync(placeId, ct);
        public Task<string?> GetIconUrlAsync(long universeId, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<RobloxUser?> GetUserAsync(long userId, CancellationToken ct = default) => Task.FromResult<RobloxUser?>(null);
    }

    private PlayHistoryService Service() =>
        new(_session, _experiences, _settings, NullLogger<PlayHistoryService>.Instance, Path.Combine(_dir, "History.json"));

    private static RobloxActivity Game(long place, string job = "job", int startMinute = 0) =>
        new(RobloxActivityKind.InGame, Start.AddMinutes(startMinute), place, null, job);

    private static RobloxActivity Menu() => new(RobloxActivityKind.Menu, Start);

    [Fact]
    public void Time_in_a_game_is_counted_when_leaving_it()
    {
        var history = Service();

        history.Observe(Game(5), Start);
        history.Observe(Menu(), Start.AddMinutes(10));

        var game = Assert.Single(history.Games);
        Assert.Equal(5, game.PlaceId);
        Assert.Equal(600, game.Seconds);
        Assert.Equal(1, game.Sessions);
    }

    [Fact]
    public void Menu_time_is_not_counted()
    {
        var history = Service();

        history.Observe(Menu(), Start);
        history.Observe(null, Start.AddHours(1));

        Assert.Empty(history.Games);
    }

    [Fact]
    public void Details_arriving_during_a_game_do_not_split_it()
    {
        var history = Service();

        history.Observe(Game(5), Start);
        history.Observe(Game(5) with { UniverseId = 9, UserId = 3 }, Start.AddMinutes(1));
        history.Observe(null, Start.AddMinutes(10));

        Assert.Equal(1, Assert.Single(history.Games).Sessions);
    }

    [Fact]
    public void Moving_to_another_place_or_server_starts_a_new_session()
    {
        var history = Service();

        history.Observe(Game(5, "a"), Start);
        history.Observe(Game(5, "b", 10), Start.AddMinutes(10));
        history.Observe(Game(6, "c", 20), Start.AddMinutes(20));
        history.Observe(null, Start.AddMinutes(50));

        Assert.Equal(2, history.Games.Count);
        Assert.Equal(6, history.Games[0].PlaceId); // most played first
        Assert.Equal(1800, history.Games[0].Seconds);
        Assert.Equal(1200, history.Games[1].Seconds);
        Assert.Equal(3, history.TotalSessions);
        Assert.Equal(3000, history.TotalSeconds);
    }

    [Fact]
    public void Very_short_visits_are_ignored()
    {
        var history = Service();

        history.Observe(Game(5), Start);
        history.Observe(null, Start.AddSeconds(5));

        Assert.Empty(history.Games);
    }

    [Fact]
    public void Nothing_is_recorded_when_turned_off()
    {
        _settings.Current.Launcher.RecordPlayHistory = false;
        var history = Service();

        history.Observe(Game(5), Start);
        history.Observe(null, Start.AddHours(1));

        Assert.Empty(history.Games);
        Assert.False(history.IsRecording);
    }

    [Fact]
    public void History_survives_a_restart_and_clears()
    {
        var first = Service();
        first.Observe(Game(5), Start);
        first.Observe(null, Start.AddMinutes(10));

        var second = Service();
        Assert.Equal(600, second.SecondsFor(5));

        second.Clear();
        Assert.Empty(Service().Games);
    }

    [Fact]
    public void Totals_stay_correct_after_old_sessions_are_trimmed()
    {
        var history = Service();
        for (var i = 0; i < PlayHistoryService.MaxSessions + 5; i++)
        {
            history.Observe(Game(5, "job" + i), Start);
            history.Observe(null, Start.AddMinutes(1));
        }

        Assert.Equal(PlayHistoryService.MaxSessions + 5, history.TotalSessions);
        Assert.Equal((PlayHistoryService.MaxSessions + 5) * 60d, history.TotalSeconds);
        Assert.Equal(PlayHistoryService.MaxSessions * 60d, history.SecondsSince(Start.AddDays(-1)));
    }

    [Fact]
    public void A_game_in_progress_is_saved_when_Nexstrap_closes()
    {
        var history = Service();
        _session.Set(Game(5) with { SinceUtc = DateTime.UtcNow.AddMinutes(-5) });

        history.Flush();

        Assert.InRange(history.SecondsFor(5), 299, 310);
    }

    [Fact]
    public async Task Unknown_games_get_their_name_looked_up()
    {
        var history = Service();
        history.Observe(Game(5), Start);
        history.Observe(null, Start.AddMinutes(1));
        await Task.Delay(50);

        Assert.Equal("Looked up game", Assert.Single(history.Games).Name);
    }

    [Fact]
    public void Games_started_from_Nexstrap_keep_their_saved_name()
    {
        _settings.Current.Launcher.RecentExperiences.Add(new RecentExperience { PlaceId = 5, Name = "Saved name" });
        var history = Service();
        history.Observe(Game(5), Start);
        history.Observe(null, Start.AddMinutes(1));

        Assert.Equal("Saved name", Assert.Single(history.Games).Name);
    }

    [Theory]
    [InlineData(30, "under a minute")]
    [InlineData(600, "10 min")]
    [InlineData(5400, "1 h 30 min")]
    public void Durations_read_naturally(double seconds, string expected) =>
        Assert.Equal(expected, PlayHistoryService.FormatDuration(seconds));

    private sealed class NoServer : ILastServerService
    {
        public LastServerInfo? Available => null;
        public Task<LaunchResult?> RejoinAsync() => Task.FromResult<LaunchResult?>(null);
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class NoLaunches : ILaunchCoordinator
    {
        public event EventHandler? RecentsChanged { add { } remove { } }
        public Task<LaunchResult?> LaunchAppAsync() => Task.FromResult<LaunchResult?>(null);
        public Task<LaunchResult?> JoinAsync(long placeId, string? name, string? serverId = null) => Task.FromResult<LaunchResult?>(null);
    }

    [Fact]
    public void Tray_menu_lists_open_play_recent_games_and_quit()
    {
        for (var i = 0; i < 7; i++) _settings.Current.Launcher.RecentExperiences.Add(new RecentExperience { PlaceId = i + 1, Name = $"Game & {i}" });
        using var menu = new System.Windows.Forms.ContextMenuStrip();
        using var tray = new Nexstrap.Services.Shell.TrayService(_settings, new NoLaunches(), new NoServer());

        tray.Fill(menu);
        tray.Fill(menu); // reopening must not add everything twice

        Assert.Equal(["Open Nexstrap", "Play Roblox", "Recent games", "", "Quit Nexstrap"], menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Select(i => i.Text ?? ""));
        var games = (System.Windows.Forms.ToolStripMenuItem)menu.Items[2];
        Assert.Equal(Nexstrap.Services.Shell.TrayService.MaxRecentGames, games.DropDownItems.Count);
        Assert.Equal("Game && 0", games.DropDownItems[0].Text);
    }

    [Fact]
    public void Tray_menu_leaves_out_recent_games_when_there_are_none()
    {
        using var menu = new System.Windows.Forms.ContextMenuStrip();
        using var tray = new Nexstrap.Services.Shell.TrayService(_settings, new NoLaunches(), new NoServer());

        tray.Fill(menu);

        Assert.Equal(4, menu.Items.Count);
    }

    [Fact]
    public void Tray_flag_is_read_and_does_not_count_as_a_link()
    {
        var options = StartupOptions.Parse(["--tray"]);

        Assert.True(options.Tray);
        Assert.Empty(options.Positional);
    }

    [Fact]
    public void Hidden_start_adds_the_tray_flag_and_survives_a_move()
    {
        const string key = @"Software\NexstrapHistoryTests\Run";
        try
        {
            var startup = new WindowsStartup(key, @"C:\A\Nexstrap.exe");
            startup.Enable(hidden: true);
            Assert.True(startup.IsEnabled);
            Assert.Equal("\"C:\\A\\Nexstrap.exe\" --tray", WindowsStartup.Read(key));

            WindowsStartup.Retarget(key, @"C:\B\Nexstrap.exe");
            Assert.Equal("\"C:\\B\\Nexstrap.exe\" --tray", WindowsStartup.Read(key));
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\NexstrapHistoryTests", throwOnMissingSubKey: false);
        }
    }
}
