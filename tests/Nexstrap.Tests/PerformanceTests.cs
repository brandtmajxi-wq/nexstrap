using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Discord;
using Nexstrap.Services.FastFlags;
using Nexstrap.Services.Performance;

namespace Nexstrap.Tests;

public class PerformancePlannerTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private FastFlagService Flags() => new(NullLogger<FastFlagService>.Instance, _dir.File("flags.json"), _dir.File("backups"));

    [Theory]
    [InlineData(null, 60)]
    [InlineData(30, 60)]
    [InlineData(59, 60)]
    [InlineData(60, 60)]
    [InlineData(75, 60)]
    [InlineData(120, 120)]
    [InlineData(144, 144)]
    [InlineData(165, 144)]
    [InlineData(240, 240)]
    [InlineData(360, 240)]
    public void Recommends_highest_cap_the_display_can_show(int? hz, int expected) =>
        Assert.Equal(expected, PerformancePlanner.RecommendedCap(hz));

    [Fact]
    public void Presets_follow_the_display_unless_fixed()
    {
        var balanced = PerformancePlanner.Presets.Single(p => p.Name == "Balanced");
        var saver = PerformancePlanner.Presets.Single(p => p.Name == "Power saver");
        Assert.Equal(144, PerformancePlanner.ResolveCap(balanced, 144));
        Assert.Equal(60, PerformancePlanner.ResolveCap(saver, 240));
        Assert.All(PerformancePlanner.Presets, p => Assert.InRange(p.Quality, 0, 10));
    }

    [Fact]
    public void Only_one_graphics_api_flag_is_kept()
    {
        var flags = Flags();
        PerformancePlanner.ApplyApi(flags, GraphicsApi.Vulkan);
        Assert.Equal(GraphicsApi.Vulkan, PerformancePlanner.ReadApi(flags.Flags));

        PerformancePlanner.ApplyApi(flags, GraphicsApi.Direct3D11);
        Assert.Equal("FFlagDebugGraphicsPreferD3D11", Assert.Single(flags.Flags).Name);

        PerformancePlanner.ApplyApi(flags, GraphicsApi.Automatic);
        Assert.Empty(flags.Flags);
    }

    [Fact]
    public void Every_graphics_preference_uses_an_allowlisted_flag()
    {
        var flags = Flags();
        PerformancePlanner.ApplyApi(flags, GraphicsApi.OpenGL);
        PerformancePlanner.ApplyMsaa(flags, 4);
        PerformancePlanner.ApplyTextures(flags, 2);

        Assert.Equal(4, flags.Flags.Count);
        Assert.All(flags.Flags, f => Assert.True(FastFlagCatalog.IsAllowlisted(f.Name), f.Name));
        Assert.Equal(4, PerformancePlanner.ReadInt(flags.Flags, PerformancePlanner.ForceMsaa));
        Assert.Equal(2, PerformancePlanner.ReadInt(flags.Flags, PerformancePlanner.TextureOverride));

        PerformancePlanner.ApplyMsaa(flags, -1);
        PerformancePlanner.ApplyTextures(flags, -1);
        Assert.Single(flags.Flags);
    }

    private static IReadOnlyList<StatusLine> Status(int? cap, int? hz, bool exists = true, bool running = false, bool nexstrap = true,
        params FastFlagEntry[] flags) => PerformancePlanner.BuildStatus(cap, hz, exists, running, nexstrap, flags);

    [Fact]
    public void Status_explains_cap_against_the_display()
    {
        Assert.Equal(StatusLevel.Good, Status(144, 144)[0].Level);
        Assert.Contains("can't appear", Status(240, 60)[0].Text);
        Assert.Contains("up to 144", Status(60, 144)[0].Text);
        Assert.Contains("hasn't created", Status(null, 60, exists: false)[0].Text);
    }

    [Fact]
    public void Status_warns_about_roblox_running_old_fps_flag_and_wrong_build()
    {
        Assert.Contains(Status(60, 60, running: true), l => l.Text.Contains("Close it"));
        Assert.Contains(Status(60, 60, flags: new FastFlagEntry { Name = PerformancePlanner.OldFpsFlag, Value = "240" }),
            l => l.Text.Contains("ignores"));
        Assert.Contains(Status(60, 60, nexstrap: false, flags: new FastFlagEntry { Name = PerformancePlanner.ForceMsaa, Value = "4" }),
            l => l.Level == StatusLevel.Warning && l.Text.Contains("won't apply"));
    }

    [Fact]
    public void Display_reports_a_plausible_refresh_rate()
    {
        var hz = new DisplayInfo().RefreshRate;
        if (hz is not null) Assert.InRange(hz.Value, 24, 1000);
    }
}

public class CustomPresenceTextTests
{
    [Theory]
    [InlineData(null, "roblox")]
    [InlineData("", "roblox")]
    [InlineData("  ", "roblox")]
    [InlineData("Zov_Logo", "zov_logo")]
    [InlineData("https://cdn.example.com/Zov.PNG", "https://cdn.example.com/Zov.PNG")]
    [InlineData("http://insecure.example/x.png", "roblox")]
    [InlineData("C:\\Users\\me\\logo.png", "roblox")]
    [InlineData("has spaces", "roblox")]
    public void Card_image_accepts_links_and_asset_names_only(string? input, string expected) =>
        Assert.Equal(expected, RichPresenceService.CardImage(new DiscordSettings { MenuImage = input }));

    [Fact]
    public void Card_image_is_used_in_the_menu_and_as_the_game_fallback()
    {
        var options = new DiscordSettings { MenuImage = "https://cdn.example.com/zov.png" };
        Assert.Equal(options.MenuImage, (string?)RichPresenceService.BuildMenu(DateTime.UtcNow, options)["assets"]!["large_image"]);

        var info = new Nexstrap.Services.Roblox.ExperienceInfo(1, 2, "Game", "Maker", 3);
        var game = RichPresenceService.BuildGame(info, "https://icon", 1, DateTime.UtcNow, options);
        Assert.Equal("https://icon", (string?)game["assets"]!["large_image"]);
        Assert.Equal(options.MenuImage, (string?)game["assets"]!["small_image"]);
    }

    [Fact]
    public void Menu_card_uses_custom_text_and_falls_back_when_blank()
    {
        var custom = RichPresenceService.BuildMenu(DateTime.UtcNow, new DiscordSettings { MenuTopLine = "Inside Zovstrap", MenuBottomLine = "Browsing Zovstrap" });
        Assert.Equal("Inside Zovstrap", (string?)custom["details"]);
        Assert.Equal("Browsing Zovstrap", (string?)custom["state"]);

        var blank = RichPresenceService.BuildMenu(DateTime.UtcNow, new DiscordSettings { MenuTopLine = " ", MenuBottomLine = "" });
        Assert.Equal(DiscordSettings.DefaultMenuTopLine, (string?)blank["details"]);
        Assert.Equal(DiscordSettings.DefaultMenuBottomLine, (string?)blank["state"]);
    }
}
