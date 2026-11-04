using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Tests;

public class RobloxFontTests : IDisposable
{
    private const string Family = """{ "name": "Builder Sans", "faces": [ { "name": "Regular", "weight": 400, "style": "normal", "assetId": "rbxassetid://16480735997" }, { "name": "Bold", "weight": 700, "style": "normal", "assetId": "rbxasset://fonts/Bold.ttf" } ] }""";

    private readonly TempDir _dir = new();
    private readonly MemorySettings _settings = new();
    private readonly RobloxFontService _service;
    private readonly string _build, _families, _font;

    public RobloxFontTests()
    {
        _service = new RobloxFontService(_settings, NullLogger<RobloxFontService>.Instance, _dir.File("Fonts"));
        _build = _dir.File("version-x");
        _families = Path.Combine(_build, "content", "fonts", "families");
        Directory.CreateDirectory(_families);
        File.WriteAllText(Path.Combine(_families, "BuilderSans.json"), Family);
        File.WriteAllText(Path.Combine(_families, "NotoSansCJKFallback.json"), Family);
        _font = _dir.File("custom.ttf");
        File.WriteAllText(_font, "font bytes");
    }

    public void Dispose() => _dir.Dispose();

    private RobloxInstallation Install(InstallationSource source = InstallationSource.Nexstrap) =>
        new(source, _build, Path.Combine(_build, "RobloxPlayerBeta.exe"), "version-x", "1", DateTime.Now);

    private string Sans => File.ReadAllText(Path.Combine(_families, "BuilderSans.json"));

    private void TurnOn()
    {
        _settings.Current.RobloxFont.Enabled = true;
        _settings.Current.RobloxFont.FontFile = _font;
    }

    [Fact]
    public void Rewrites_every_face_and_keeps_the_rest_of_the_file()
    {
        var node = JsonNode.Parse(RobloxFontService.RewriteFamily(Family, "rbxasset://fonts/X.ttf")!)!;
        Assert.Equal("Builder Sans", (string?)node["name"]);
        Assert.Equal(2, node["faces"]!.AsArray().Count);
        Assert.All(node["faces"]!.AsArray(), f => Assert.Equal("rbxasset://fonts/X.ttf", (string?)f!["assetId"]));
        Assert.Equal(700, (int?)node["faces"]![1]!["weight"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "name": "x" }""")]
    [InlineData("""{ "faces": [] }""")]
    public void Skips_files_that_arent_font_families(string json) =>
        Assert.Null(RobloxFontService.RewriteFamily(json, "x"));

    [Fact]
    public void Applies_then_restores_the_originals_exactly()
    {
        TurnOn();
        _service.ApplyTo(Install());

        Assert.Contains("NexstrapCustomFont.ttf", Sans);
        Assert.True(File.Exists(Path.Combine(_build, "content", "fonts", "NexstrapCustomFont.ttf")));
        Assert.Equal(Family, File.ReadAllText(Path.Combine(_families, "NotoSansCJKFallback.json"))); // left alone

        _service.ApplyTo(Install()); // a second launch mustn't back up the changed files

        _settings.Current.RobloxFont.Enabled = false;
        _service.ApplyTo(Install());

        Assert.Equal(Family, Sans);
        Assert.False(File.Exists(Path.Combine(_build, "content", "fonts", "NexstrapCustomFont.ttf")));
        Assert.False(Directory.Exists(Path.Combine(_build, RobloxFontService.BackupFolder)));
    }

    [Fact]
    public void Never_touches_other_launchers_builds()
    {
        TurnOn();
        _service.ApplyTo(Install(InstallationSource.Voidstrap));
        Assert.Equal(Family, Sans);
        Assert.False(Directory.Exists(Path.Combine(_build, RobloxFontService.BackupFolder)));
    }

    [Fact]
    public void Off_by_default_and_a_missing_font_does_nothing()
    {
        Assert.False(new RobloxFontSettings().Enabled);
        _settings.Current.RobloxFont.Enabled = true;
        _settings.Current.RobloxFont.FontFile = _dir.File("gone.ttf");
        _service.ApplyTo(Install());
        Assert.Equal(Family, Sans);
    }

    [Fact]
    public void Rejects_unsupported_and_broken_font_files()
    {
        File.WriteAllText(_dir.File("a.ttc"), "x");
        Assert.Contains(".ttf or .otf", _service.SetFontFile(_dir.File("a.ttc")));
        Assert.Contains("couldn't be read", _service.SetFontFile(_font)); // not a real font
        Assert.Null(_settings.Current.RobloxFont.FontFile);
    }

    /// <summary>Opt-in: works on a temp copy of the real family files, never the real build.</summary>
    [Fact]
    public void Round_trips_a_copy_of_real_roblox_family_files()
    {
        if (Environment.GetEnvironmentVariable("NEXSTRAP_LIVE_TESTS") != "1") return;
        var versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nexstrap", "Versions");
        var real = Directory.GetDirectories(versions)
            .Select(d => Path.Combine(d, "content", "fonts", "families"))
            .First(Directory.Exists);

        File.Delete(Path.Combine(_families, "BuilderSans.json"));
        File.Delete(Path.Combine(_families, "NotoSansCJKFallback.json"));
        var originals = Directory.GetFiles(real, "*.json").ToDictionary(Path.GetFileName, File.ReadAllText);
        foreach (var (name, text) in originals) File.WriteAllText(Path.Combine(_families, name!), text);

        TurnOn();
        _service.ApplyTo(Install());
        var changed = originals.Keys.Count(n => File.ReadAllText(Path.Combine(_families, n!)).Contains("NexstrapCustomFont.ttf"));
        Assert.Equal(originals.Count - 1, changed); // everything except the CJK fallback

        _settings.Current.RobloxFont.Enabled = false;
        _service.ApplyTo(Install());
        foreach (var (name, text) in originals) Assert.Equal(text, File.ReadAllText(Path.Combine(_families, name!)));
    }

    [Fact]
    public void Accepts_a_real_installed_font()
    {
        var arial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (!File.Exists(arial)) return;
        Assert.Null(_service.SetFontFile(arial));
        Assert.True(File.Exists(_settings.Current.RobloxFont.FontFile));
        Assert.Equal("arial", _settings.Current.RobloxFont.DisplayName);
    }
}
