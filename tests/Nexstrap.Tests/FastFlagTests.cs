using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.FastFlags;

namespace Nexstrap.Tests;

public class FastFlagCatalogTests
{
    [Theory]
    [InlineData("FFlagDebugSkyGray", FastFlagKind.Bool)]
    [InlineData("DFFlagDisableDPIScale", FastFlagKind.Bool)]
    [InlineData("SFFlagSomething", FastFlagKind.Bool)]
    [InlineData("FIntDebugForceMSAASamples", FastFlagKind.Int)]
    [InlineData("DFIntTextureQualityOverride", FastFlagKind.Int)]
    [InlineData("FLogNetwork", FastFlagKind.Int)]
    [InlineData("DFStringSomething", FastFlagKind.String)]
    public void Infers_kind_from_prefix(string name, FastFlagKind kind) => Assert.Equal(kind, FastFlagCatalog.KindOf(name));

    [Theory]
    [InlineData("")]
    [InlineData("SkyGray")]
    [InlineData("FFlag")]
    [InlineData("FFlag Debug")]
    [InlineData("XFlagDebug")]
    public void Rejects_bad_names(string name) => Assert.NotNull(FastFlagCatalog.ValidateName(name));

    [Theory]
    [InlineData("FFlagDebugSkyGray", "true", null)]
    [InlineData("FFlagDebugSkyGray", "yes", "bool")]
    [InlineData("FIntDebugForceMSAASamples", "4", null)]
    [InlineData("FIntDebugForceMSAASamples", "-1", null)]
    [InlineData("FIntDebugForceMSAASamples", "4.5", "number")]
    [InlineData("FIntDebugForceMSAASamples", "99999999999", "number")]
    [InlineData("DFStringAnything", "hello world", null)]
    public void Validates_values(string name, string value, string? errorKind)
    {
        var error = FastFlagCatalog.ValidateValue(name, value);
        if (errorKind is null) Assert.Null(error);
        else Assert.NotNull(error);
    }

    [Fact]
    public void Normalises_values()
    {
        Assert.Equal("True", FastFlagCatalog.Normalise("FFlagX1", " true "));
        Assert.Equal("False", FastFlagCatalog.Normalise("FFlagX1", "FALSE"));
        Assert.Equal("42", FastFlagCatalog.Normalise("FIntX1", " +42 "));
    }

    [Fact]
    public void Allowlist_matches_roblox_announcement()
    {
        var allowlisted = FastFlagCatalog.Known.Where(k => k.Allowlisted).Select(k => k.Name).ToList();
        Assert.Equal(18, allowlisted.Count);
        Assert.Contains("FFlagDebugGraphicsPreferVulkan", allowlisted);
        Assert.Contains("FIntGrassMovementReducedMotionFactor", allowlisted);
        Assert.False(FastFlagCatalog.IsAllowlisted("DFIntTaskSchedulerTargetFps"));
        Assert.All(FastFlagCatalog.Known, k => Assert.Equal(k.Kind, FastFlagCatalog.KindOf(k.Name)));
        Assert.All(FastFlagCatalog.Known.Where(k => k.SuggestedValue is not null),
            k => Assert.Null(FastFlagCatalog.ValidateValue(k.Name, k.SuggestedValue)));
    }
}

public class FastFlagServiceTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private FastFlagService Create() => new(NullLogger<FastFlagService>.Instance, _dir.File("FastFlags.json"), _dir.File("Backups"));

    private RobloxInstallation Install(InstallationSource source)
    {
        var dir = _dir.File(source + "-build");
        Directory.CreateDirectory(dir);
        return new RobloxInstallation(source, dir, Path.Combine(dir, "RobloxPlayerBeta.exe"), "version-x", "1", DateTime.Now);
    }

    [Fact]
    public void Set_validates_normalises_and_persists()
    {
        var service = Create();
        Assert.NotNull(service.Set("FIntDebugForceMSAASamples", "lots"));
        Assert.Null(service.Set("FIntDebugForceMSAASamples", " 4 "));
        Assert.NotNull(service.Set("fflagdebugskygray", "true")); // Roblox names are case-sensitive
        Assert.Null(service.Set("FFlagDebugSkyGray", "true"));
        Assert.Null(service.Set("FFlagDebugSkyGray", "false"));      // same name → updated, not duplicated

        var reloaded = Create();
        Assert.Equal(2, reloaded.Flags.Count);
        Assert.Equal("4", reloaded.Flags.Single(f => f.Name == "FIntDebugForceMSAASamples").Value);
        Assert.Equal("False", reloaded.Flags.Single(f => f.Name == "FFlagDebugSkyGray").Value);
    }

    [Fact]
    public void Disabled_flags_are_kept_but_not_exported()
    {
        var service = Create();
        service.Set("FFlagDebugSkyGray", "True");
        service.Set("FIntDebugForceMSAASamples", "8");
        service.SetEnabled("FFlagDebugSkyGray", false);

        var exported = JsonNode.Parse(service.Export())!.AsObject();
        Assert.Single(exported);
        Assert.Equal("8", (string?)exported["FIntDebugForceMSAASamples"]);
        Assert.Equal(2, Create().Flags.Count);
    }

    [Fact]
    public void Import_accepts_strings_bools_and_numbers_and_reports_skips()
    {
        var service = Create();
        service.Set("FIntDebugForceMSAASamples", "2");

        var result = service.Import("""
            {
              "FIntDebugForceMSAASamples": 8,
              "FFlagDebugSkyGray": true,
              "DFIntTextureQualityOverride": "3",
              "NotAFlag": 1,
              "FIntBroken": "abc",
              "FFlagNested": { "x": 1 },
            }
            """, replaceExisting: false);

        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(3, result.Skipped.Count);
        Assert.Equal("8", service.Flags.Single(f => f.Name == "FIntDebugForceMSAASamples").Value);
        Assert.Equal("True", service.Flags.Single(f => f.Name == "FFlagDebugSkyGray").Value);
        Assert.Single(service.ListBackups()); // backup before the import
    }

    [Fact]
    public void Import_rejects_non_objects()
    {
        Assert.Throws<InvalidDataException>(() => Create().Import("[1,2,3]", false));
        Assert.Throws<InvalidDataException>(() => Create().Import("{ nope", false));
    }

    [Fact]
    public void Reset_and_restore_round_trip_through_backups()
    {
        var service = Create();
        service.Set("FFlagDebugSkyGray", "True");
        service.Set("FIntDebugForceMSAASamples", "4");

        service.Reset();
        Assert.Empty(service.Flags);

        var backup = service.ListBackups().First(b => b.Reason == "Before reset");
        Assert.Equal(2, backup.FlagCount);
        service.Restore(backup);
        Assert.Equal(2, service.Flags.Count);
    }

    [Fact]
    public void Writes_only_into_nexstrap_builds()
    {
        var service = Create();
        service.Set("FIntDebugForceMSAASamples", "4");
        service.Set("FFlagDebugSkyGray", "True");
        service.SetEnabled("FFlagDebugSkyGray", false);

        var ours = Install(InstallationSource.Nexstrap);
        service.ApplyTo(ours);
        var written = JsonNode.Parse(File.ReadAllText(Path.Combine(ours.Directory, "ClientSettings", "ClientAppSettings.json")))!.AsObject();
        Assert.Single(written);
        Assert.Equal("4", (string?)written["FIntDebugForceMSAASamples"]);

        var theirs = Install(InstallationSource.Voidstrap);
        service.ApplyTo(theirs);
        Assert.False(Directory.Exists(Path.Combine(theirs.Directory, "ClientSettings")));
    }

    [Fact]
    public void Reads_other_launchers_flags_without_touching_them()
    {
        var theirs = Install(InstallationSource.Voidstrap);
        var file = Path.Combine(theirs.Directory, "ClientSettings", "ClientAppSettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, """{ "DFFlagDisableDPIScale": "True" }""");
        var before = File.GetLastWriteTimeUtc(file);

        var service = Create();
        var json = service.ReadFromInstallation(theirs);
        service.Import(json!, false);

        Assert.Equal("True", service.Flags.Single().Value);
        Assert.Equal(before, File.GetLastWriteTimeUtc(file));
        Assert.Null(service.ReadFromInstallation(Install(InstallationSource.Bloxstrap)));
    }

    [Fact]
    public void Hand_edited_garbage_is_dropped_on_load()
    {
        File.WriteAllText(_dir.File("FastFlags.json"),
            """{ "Flags": [ { "Name": "FFlagOk", "Value": "True" }, { "Name": "bad name", "Value": "1" } ] }""");
        Assert.Equal("FFlagOk", Assert.Single(Create().Flags).Name);
    }
}
