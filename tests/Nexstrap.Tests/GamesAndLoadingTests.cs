using System.IO;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Tests;

public class LaunchScreenTests
{
    [Theory]
    [InlineData(false, false, 1, false)]
    [InlineData(true, false, 1, true)]    // Roblox's window opened
    [InlineData(false, true, 1, true)]    // Roblox exited
    [InlineData(false, false, 20, true)]  // never stays up forever
    public void Closes_at_the_right_time(bool hasWindow, bool exited, int seconds, bool expected) =>
        Assert.Equal(expected, LaunchScreenService.ShouldClose(hasWindow, exited, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(20)));

    [Fact]
    public void Presets_have_valid_colours()
    {
        Assert.NotEmpty(LaunchScreenService.Presets);
        Assert.All(LaunchScreenService.Presets, p =>
        {
            Assert.True(ColorUtil.TryParseHex(p.BackgroundColor, out _));
            Assert.True(ColorUtil.TryParseHex(p.AccentColor, out _));
        });
    }
}

public class GameShortcutTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Writes_a_url_shortcut_with_the_join_link()
    {
        var path = new GameShortcutService(_dir.Path, @"C:\Apps\Nexstrap.exe").CreateOnDesktop(1818, "Crossroads");
        Assert.Equal("Crossroads.url", Path.GetFileName(path));
        var text = File.ReadAllText(path);
        Assert.Contains("[InternetShortcut]", text);
        Assert.Contains("URL=roblox://placeId=1818", text);
        Assert.Contains(@"IconFile=C:\Apps\Nexstrap.exe", text);
    }

    [Fact]
    public void Never_overwrites_an_existing_file()
    {
        var service = new GameShortcutService(_dir.Path, null);
        File.WriteAllText(_dir.File("Game.url"), "mine");
        Assert.Equal("Game (2).url", Path.GetFileName(service.CreateOnDesktop(1, "Game")));
        Assert.Equal("mine", File.ReadAllText(_dir.File("Game.url")));
    }

    [Theory]
    [InlineData("[v1.2] Wings: of/Glory?", "[v1.2] Wings_ of_Glory_")]
    [InlineData("...", "Roblox game")]
    [InlineData("", "Roblox game")]
    public void Makes_game_names_safe_for_files(string name, string expected) =>
        Assert.Equal(expected, GameShortcutService.SafeFileName(name));
}

public class RecentGamesTests
{
    private static LaunchCoordinator Create(MemorySettings settings) =>
        new(null!, null!, null!, null!, null!, settings, null!, null!);

    [Fact]
    public void Keeps_newest_first_without_duplicates_and_preserves_names()
    {
        var settings = new MemorySettings();
        var coordinator = Create(settings);
        var raised = 0;
        coordinator.RecentsChanged += (_, _) => raised++;

        coordinator.RememberRecent(1, "One");
        coordinator.RememberRecent(2, "Two");
        coordinator.RememberRecent(1, null);

        var list = settings.Current.Launcher.RecentExperiences;
        Assert.Equal([1L, 2L], list.Select(r => r.PlaceId));
        Assert.Equal("One", list[0].Name);
        Assert.Equal(3, raised);
    }

    [Fact]
    public void List_is_capped()
    {
        var settings = new MemorySettings();
        var coordinator = Create(settings);
        for (var i = 1; i <= LaunchCoordinator.MaxRecent + 5; i++) coordinator.RememberRecent(i, null);
        Assert.Equal(LaunchCoordinator.MaxRecent, settings.Current.Launcher.RecentExperiences.Count);
    }
}
