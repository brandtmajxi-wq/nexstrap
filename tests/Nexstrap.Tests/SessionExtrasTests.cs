using System.Text.Json.Nodes;
using Nexstrap.Models;
using Nexstrap.Services.Discord;
using Xunit;

namespace Nexstrap.Tests;

public class SessionExtrasTests
{
    [Fact]
    public void Keep_awake_is_off_by_default()
    {
        Assert.False(new AppSettings().Launcher.KeepAwakeWhilePlaying);
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(2, "2")]
    [InlineData(3, "3")]
    [InlineData(4, "4")]
    public void Top_games_show_their_rank(int rank, string medal)
    {
        Assert.Equal(medal, new Nexstrap.ViewModels.Pages.TopGame("Game", "1 h", 100, rank).Medal);
    }

    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData(40, "Good")]
    [InlineData(150, "Ok")]
    [InlineData(260, "Bad")]
    public void Server_ping_gets_a_colour(int? ping, string quality)
    {
        Assert.Equal(quality, new Nexstrap.Services.Roblox.GameServer("id", 5, 10, 60, ping).PingQuality);
    }

    [Fact]
    public void Background_mute_is_off_by_default()
    {
        Assert.False(new AppSettings().Launcher.MuteRobloxInBackground);
    }

    [Fact]
    public void The_volume_mixer_can_be_read_without_changing_anything()
    {
        // Only reads each sound output's process; nothing is muted.
        var sessions = Nexstrap.Services.Roblox.BackgroundMute.Sessions().ToList();
        Assert.All(sessions, s => Assert.True(s.ProcessId >= 0));
        foreach (var s in sessions) System.Runtime.InteropServices.Marshal.ReleaseComObject(s.Volume);
    }

    private static JsonObject Game() => RichPresenceService.BuildGame(null, null, 920587237, DateTime.UtcNow, new DiscordSettings());

    [Fact]
    public void Dev_activity_gets_a_dev_tag_badge_and_button()
    {
        var activity = RichPresenceService.DevActivity(Game(), clipping: false);

        Assert.EndsWith("DEV", activity["state"]!.GetValue<string>());
        Assert.Contains("DEV build", activity["assets"]!["large_text"]!.GetValue<string>());
        Assert.NotNull(activity["assets"]!["small_image"]);
        var buttons = activity["buttons"]!.AsArray();
        Assert.InRange(buttons.Count, 1, 2);
        Assert.Contains(buttons, b => b!["label"]!.GetValue<string>() == "Nexstrap DEV");
    }

    [Fact]
    public void Get_nexstrap_button_is_off_by_default_and_added_when_on()
    {
        Assert.Null(Game()["buttons"]?.AsArray().FirstOrDefault(b => b!["label"]!.GetValue<string>() == RichPresenceService.NexstrapButtonLabel));

        var options = new DiscordSettings { ShowNexstrapButton = true };
        var game = RichPresenceService.BuildGame(null, null, 920587237, DateTime.UtcNow, options);
        var labels = game["buttons"]!.AsArray().Select(b => b!["label"]!.GetValue<string>()).ToList();
        Assert.Equal(["View game", RichPresenceService.NexstrapButtonLabel], labels);

        var menu = RichPresenceService.BuildMenu(DateTime.UtcNow, options);
        Assert.Single(menu["buttons"]!.AsArray());
    }

    [Fact]
    public void Dev_activity_reuses_the_get_nexstrap_button()
    {
        var game = RichPresenceService.BuildGame(null, null, 1, DateTime.UtcNow, new DiscordSettings { ShowNexstrapButton = true });
        var buttons = RichPresenceService.DevActivity(game, false)["buttons"]!.AsArray();
        Assert.Equal(2, buttons.Count);
        Assert.Contains(buttons, b => b!["label"]!.GetValue<string>() == "Nexstrap DEV");
    }

    [Fact]
    public void Dev_activity_shows_clipping()
    {
        var activity = RichPresenceService.DevActivity(Game(), clipping: true);
        Assert.StartsWith("🔴 Clipping", activity["state"]!.GetValue<string>());
    }

    [Fact]
    public void Dev_activity_never_has_more_than_two_buttons()
    {
        var game = Game();
        game["buttons"] = new JsonArray(new JsonObject { ["label"] = "a", ["url"] = "https://a.b" }, new JsonObject { ["label"] = "b", ["url"] = "https://b.c" });
        Assert.Equal(2, RichPresenceService.DevActivity(game, false)["buttons"]!.AsArray().Count);
    }
}
