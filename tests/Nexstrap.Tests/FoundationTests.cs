using System.IO;
using System.Windows.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexstrap-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}

public class JsonFileStoreTests
{
    [Fact]
    public void Missing_file_returns_defaults()
    {
        using var dir = new TempDir();
        var store = new JsonFileStore<AppSettings>(dir.File("s.json"), NullLogger.Instance);
        Assert.Equal(AppTheme.Dark, store.Load().Appearance.Theme);
    }

    [Fact]
    public void Round_trips_values()
    {
        using var dir = new TempDir();
        var store = new JsonFileStore<AppSettings>(dir.File("s.json"), NullLogger.Instance);
        var s = new AppSettings { Appearance = { Theme = AppTheme.Light, AccentColor = "#112233" } };
        store.Save(s);

        var loaded = store.Load();
        Assert.Equal(AppTheme.Light, loaded.Appearance.Theme);
        Assert.Equal("#112233", loaded.Appearance.AccentColor);
        Assert.False(File.Exists(dir.File("s.json.tmp")));
    }

    [Fact]
    public void Corrupt_file_is_quarantined_and_defaults_used()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.json"), "{ this is not json");
        var store = new JsonFileStore<AppSettings>(dir.File("s.json"), NullLogger.Instance);

        var loaded = store.Load();

        Assert.Equal(AppTheme.Dark, loaded.Appearance.Theme);
        Assert.False(File.Exists(dir.File("s.json")));
        Assert.Single(Directory.GetFiles(dir.Path, "s.json.corrupt-*"));
    }

    [Fact]
    public void Unknown_properties_and_missing_sections_are_tolerated()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("s.json"), """{ "SomethingNew": 1, "Appearance": { "Theme": "light" } }""");
        var loaded = new JsonFileStore<AppSettings>(dir.File("s.json"), NullLogger.Instance).Load();

        Assert.Equal(AppTheme.Light, loaded.Appearance.Theme);
        Assert.NotNull(loaded.Window);
    }
}

public class SettingsServiceTests
{
    [Fact]
    public void Deferred_save_eventually_writes()
    {
        using var dir = new TempDir();
        var path = dir.File("s.json");
        using var service = new SettingsService(path, NullLogger<SettingsService>.Instance);
        using var saved = new ManualResetEventSlim();
        service.Saved += (_, _) => saved.Set();

        service.Current.Appearance.FontFamily = "Bahnschrift";
        service.SaveDeferred();

        Assert.True(saved.Wait(TimeSpan.FromSeconds(5)));
        Assert.Contains("Bahnschrift", File.ReadAllText(path));
    }
}

public class ColorUtilTests
{
    [Theory]
    [InlineData("#7C6CFF", 0xFF, 0x7C, 0x6C, 0xFF)]
    [InlineData("7c6cff", 0xFF, 0x7C, 0x6C, 0xFF)]
    [InlineData("#abc", 0xFF, 0xAA, 0xBB, 0xCC)]
    [InlineData("#807C6CFF", 0x80, 0x7C, 0x6C, 0xFF)]
    public void Parses_valid_hex(string input, byte a, byte r, byte g, byte b)
    {
        Assert.True(ColorUtil.TryParseHex(input, out var c));
        Assert.Equal(Color.FromArgb(a, r, g, b), c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData(null)]
    public void Rejects_invalid_hex(string? input) => Assert.False(ColorUtil.TryParseHex(input, out _));

    [Fact]
    public void Readable_foreground_contrasts()
    {
        Assert.Equal(Colors.White, ColorUtil.ReadableForeground(Color.FromRgb(0x20, 0x20, 0x40)));
        Assert.NotEqual(Colors.White, ColorUtil.ReadableForeground(Color.FromRgb(0xF0, 0xF0, 0x80)));
    }
}

public class StartupOptionsTests
{
    [Fact]
    public void Parses_switches_and_positionals()
    {
        var o = StartupOptions.Parse(["--page", "Appearance", "roblox-player:1+launchmode:play", "--capture", "x.png"]);
        Assert.Equal("Appearance", o.StartPage);
        Assert.Equal("x.png", o.CapturePath);
        Assert.Equal(["roblox-player:1+launchmode:play"], o.Positional);
    }
}
