using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Tests;

/// <summary>Records what would have been applied to the UI, without needing a WPF Application.</summary>
internal sealed class FakeThemeService : IThemeService
{
    public List<string> Calls { get; } = [];
    public bool ImagesLoad { get; set; } = true;

    public void Apply(AppearanceSettings a) => Calls.Add("apply");
    public void ApplyTheme(AppTheme theme) => Calls.Add("theme:" + theme);
    public void ApplyAccent(Color accent) => Calls.Add("accent:" + accent);
    public void ApplyFont(string familyName) => Calls.Add("font:" + familyName);
    public void ApplyDesign(AppearanceSettings a) => Calls.Add("design");
    public bool ApplyBackground(AppearanceSettings a) { Calls.Add("bg"); return a.BackgroundImage is null || ImagesLoad; }
    public BitmapSource? LoadImage(string path) => null;
}

internal sealed class MemorySettings : ISettingsService
{
    public AppSettings Current { get; } = new();
    public int Saves { get; private set; }
    public void Save() { Saves++; Saved?.Invoke(this, EventArgs.Empty); }
    public void SaveDeferred() => Saves++;
    public event EventHandler? Saved;
}

public class AppearanceServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly MemorySettings _settings = new();
    private readonly FakeThemeService _theme = new();
    private readonly AppearanceService _service;

    public AppearanceServiceTests()
    {
        _service = new AppearanceService(_settings, _theme, NullLogger<AppearanceService>.Instance,
            _dir.File("Themes"), _dir.File("Backgrounds"));
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Accent_is_validated_normalised_and_saved()
    {
        Assert.False(_service.SetAccent("#12"));
        Assert.Equal(AppearanceSettings.DefaultAccent, _settings.Current.Appearance.AccentColor);

        Assert.True(_service.SetAccent("abc"));
        Assert.Equal("#AABBCC", _settings.Current.Appearance.AccentColor);
        Assert.Equal(1, _settings.Saves);
    }

    [Fact]
    public void Changed_fires_on_every_applied_change()
    {
        var count = 0;
        _service.Changed += (_, _) => count++;
        _service.SetTheme(AppTheme.Light);
        _service.SetFont("Bahnschrift");
        _service.SetBackgroundOpacity(0.5);
        Assert.Equal(3, count);
        Assert.Contains("theme:Light", _theme.Calls);
        Assert.Contains("font:Bahnschrift", _theme.Calls);
    }

    [Fact]
    public void Preset_changes_colours_but_keeps_font()
    {
        _service.SetFont("Bahnschrift");
        var ember = _service.Presets.Single(p => p.Name == "Ember");

        _service.ApplyProfile(ember);

        Assert.Equal("#F97316", _settings.Current.Appearance.AccentColor);
        Assert.Equal("Bahnschrift", _settings.Current.Appearance.FontFamily);
    }

    [Fact]
    public void Saved_profile_round_trips_everything()
    {
        _service.SetTheme(AppTheme.Light);
        _service.SetAccent("#22C55E");
        _service.SetFont("Bahnschrift");
        _service.SaveProfile("My: look?");   // invalid filename chars are replaced

        _service.Reset();
        Assert.Equal(AppTheme.Dark, _settings.Current.Appearance.Theme);

        var saved = Assert.Single(_service.LoadSavedProfiles());
        Assert.Equal("My: look?", saved.Name);
        Assert.True(_service.ProfileExists("My: look?"));

        _service.ApplyProfile(saved);
        Assert.Equal(AppTheme.Light, _settings.Current.Appearance.Theme);
        Assert.Equal("#22C55E", _settings.Current.Appearance.AccentColor);
        Assert.Equal("Bahnschrift", _settings.Current.Appearance.FontFamily);

        _service.DeleteProfile(saved);
        Assert.Empty(_service.LoadSavedProfiles());
    }

    [Fact]
    public void Import_rejects_garbage_and_repairs_bad_values()
    {
        File.WriteAllText(_dir.File("bad.json"), "not json at all");
        Assert.Throws<InvalidDataException>(() => _service.ImportProfile(_dir.File("bad.json")));

        File.WriteAllText(_dir.File("noname.json"), """{ "Appearance": {} }""");
        Assert.Throws<InvalidDataException>(() => _service.ImportProfile(_dir.File("noname.json")));

        File.WriteAllText(_dir.File("odd.json"), """
            { "Name": " Shared ", "Appearance": { "Theme": "Light", "AccentColor": "purple",
              "FontFamily": "", "BackgroundOpacity": 7, "BackgroundImage": "C:\\nope\\missing.png" } }
            """);
        var imported = _service.ImportProfile(_dir.File("odd.json"));

        Assert.Equal("Shared", imported.Name);
        Assert.Equal(AppearanceSettings.DefaultAccent, imported.Appearance.AccentColor);
        Assert.Equal(AppearanceSettings.DefaultFont, imported.Appearance.FontFamily);
        Assert.Equal(1, imported.Appearance.BackgroundOpacity);
        Assert.Null(imported.Appearance.BackgroundImage);
        Assert.Single(_service.LoadSavedProfiles());
    }

    [Fact]
    public void Export_then_import_preserves_profile()
    {
        _service.SetAccent("#38BDF8");
        var profile = _service.SaveProfile("Glacier copy");
        _service.ExportProfile(profile, _dir.File("export.json"));
        _service.DeleteProfile(profile);

        var back = _service.ImportProfile(_dir.File("export.json"));
        Assert.Equal("#38BDF8", back.Appearance.AccentColor);
    }

    [Fact]
    public void Background_is_copied_and_cleaned_up()
    {
        var source = _dir.File("wall.png");
        File.WriteAllBytes(source, [1, 2, 3, 4]);

        Assert.Null(_service.SetBackgroundFromFile(source));
        var copy = _settings.Current.Appearance.BackgroundImage!;
        Assert.StartsWith(_dir.File("Backgrounds"), copy);
        Assert.True(File.Exists(copy));

        _service.ClearBackground();
        Assert.Null(_settings.Current.Appearance.BackgroundImage);
        Assert.False(File.Exists(copy));
    }

    [Fact]
    public void Background_used_by_saved_profile_is_kept()
    {
        var source = _dir.File("wall.png");
        File.WriteAllBytes(source, [9, 9, 9]);
        _service.SetBackgroundFromFile(source);
        var copy = _settings.Current.Appearance.BackgroundImage!;
        _service.SaveProfile("With wallpaper");

        _service.ClearBackground();

        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void Background_rejects_unsupported_or_unreadable_files()
    {
        File.WriteAllText(_dir.File("notes.txt"), "x");
        Assert.NotNull(_service.SetBackgroundFromFile(_dir.File("notes.txt")));

        _theme.ImagesLoad = false;
        File.WriteAllText(_dir.File("fake.png"), "not an image");
        Assert.NotNull(_service.SetBackgroundFromFile(_dir.File("fake.png")));
        Assert.Null(_settings.Current.Appearance.BackgroundImage);
        Assert.Empty(Directory.GetFiles(_dir.File("Backgrounds")));
    }

    [Fact]
    public void Opacity_is_clamped()
    {
        _service.SetBackgroundOpacity(4);
        Assert.Equal(1, _settings.Current.Appearance.BackgroundOpacity);
        _service.SetBackgroundOpacity(-1);
        Assert.Equal(0, _settings.Current.Appearance.BackgroundOpacity);
    }
}

public class ThemeBrushTests
{
    [Theory]
    [InlineData(BackgroundFit.Fill, Stretch.UniformToFill)]
    [InlineData(BackgroundFit.Fit, Stretch.Uniform)]
    [InlineData(BackgroundFit.Stretch, Stretch.Fill)]
    [InlineData(BackgroundFit.Center, Stretch.None)]
    public void Fit_maps_to_stretch(BackgroundFit fit, Stretch expected)
    {
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var brush = ThemeService.CreateBrush(image, new AppearanceSettings { BackgroundFit = fit, BackgroundAnchor = BackgroundAnchor.Top, BackgroundOpacity = 0.4 });
        Assert.Equal(expected, brush.Stretch);
        Assert.Equal(AlignmentY.Top, brush.AlignmentY);
        Assert.Equal(0.4, brush.Opacity, 3);
    }

    [Fact]
    public void Tile_repeats_at_image_size()
    {
        var image = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[64], 16);
        var brush = ThemeService.CreateBrush(image, new AppearanceSettings { BackgroundFit = BackgroundFit.Tile });
        Assert.Equal(TileMode.Tile, brush.TileMode);
        Assert.Equal(4, brush.Viewport.Width);
    }
}
