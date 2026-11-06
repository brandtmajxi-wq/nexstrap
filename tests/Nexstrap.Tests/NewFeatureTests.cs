using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.FastFlags;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class PlayStatsTests
{
    private static readonly DateTime Today = new(2026, 11, 20, 12, 0, 0);

    private static PlaySession On(int daysAgo, double seconds = 600) =>
        new() { PlaceId = 1, Seconds = seconds, StartedUtc = Today.AddDays(-daysAgo).ToUniversalTime() };

    [Fact]
    public void Adds_up_each_day_and_fills_empty_days()
    {
        var days = PlayStats.LastDays([On(0, 100), On(0, 50), On(2, 30), On(30)], Today, 3);

        Assert.Equal(3, days.Count);
        Assert.Equal([30d, 0d, 150d], days.Select(d => d.Seconds));
        Assert.Equal(DateOnly.FromDateTime(Today), days[^1].Day);
    }

    [Fact]
    public void Counts_streaks()
    {
        Assert.Equal((3, 4), PlayStats.Streaks([On(0), On(1), On(2), On(5), On(6), On(7), On(8)], Today));
    }

    [Fact]
    public void Todays_missing_session_doesnt_end_a_streak_until_a_day_is_missed()
    {
        Assert.Equal(2, PlayStats.Streaks([On(1), On(2)], Today).Current);
        Assert.Equal(0, PlayStats.Streaks([On(2), On(3)], Today).Current);
    }

    [Fact]
    public void A_game_with_several_places_counts_once()
    {
        var merged = PlayStats.MergeByName(
        [
            new GamePlayStats { PlaceId = 1, Name = "Same", Seconds = 10, Sessions = 1 },
            new GamePlayStats { PlaceId = 2, Name = "Same", Seconds = 30, Sessions = 2 },
            new GamePlayStats { PlaceId = 3, Name = "", Seconds = 5, Sessions = 1 },
            new GamePlayStats { PlaceId = 4, Name = "", Seconds = 6, Sessions = 1 }
        ]);

        Assert.Equal(3, merged.Count);
        Assert.Equal(40, merged[0].Seconds);
        Assert.Equal(3, merged[0].Sessions);
        Assert.Equal(2, merged[0].PlaceId);
    }

    [Fact]
    public void No_sessions_means_no_streak() => Assert.Equal((0, 0), PlayStats.Streaks([], Today));
}

public class LogReaderTests : IDisposable
{
    private readonly string _file = Path.GetTempFileName();

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void Reads_only_the_newest_lines()
    {
        File.WriteAllLines(_file, Enumerable.Range(1, LogReader.MaxLines + 10).Select(i => "line " + i));

        var lines = LogReader.Tail(_file);

        Assert.Equal(LogReader.MaxLines, lines.Count);
        Assert.Equal("line " + (LogReader.MaxLines + 10), lines[^1]);
    }

    [Fact]
    public void Reads_a_file_another_program_is_writing_to()
    {
        using var writer = new FileStream(_file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        writer.Write("hello\n"u8);
        writer.Flush();

        Assert.Equal(["hello"], LogReader.Tail(_file));
    }

    [Fact]
    public void Filters_without_caring_about_case()
    {
        Assert.Equal(["An ERROR here"], LogReader.Filter(["fine", "An ERROR here"], "error"));
        Assert.Equal(2, LogReader.Filter(["a", "b"], "  ").Count);
    }
}

public class SettingsCatalogTests
{
    [Fact]
    public void Every_entry_has_a_real_label_and_page()
    {
        foreach (var entry in SettingsCatalog.Entries)
        {
            Assert.NotEqual(entry.LabelKey, Loc.Get(entry.LabelKey));
            Assert.True(SidebarLayout.Pages.Contains(entry.Page), entry.Page);
        }
    }

    [Fact]
    public void Finds_a_setting_by_part_of_its_name()
    {
        var result = Assert.Single(SettingsCatalog.Search("gradient"));
        Assert.Equal("Appearance", result.Page);
    }

    [Fact]
    public void Empty_search_shows_nothing_and_results_are_limited()
    {
        Assert.Empty(SettingsCatalog.Search("  "));
        Assert.True(SettingsCatalog.Search("a").Count <= SettingsCatalog.MaxResults);
    }

    [Fact]
    public void The_dev_only_winter_options_are_not_searchable() =>
        Assert.DoesNotContain(SettingsCatalog.Entries, e => e.LabelKey.Contains("Winter"));
}

public class FastFlagPresetTests
{
    [Fact]
    public void Every_preset_flag_is_on_robloxs_allowlist() =>
        Assert.All(FastFlagPresets.All.SelectMany(p => p.Flags.Keys), name => Assert.True(FastFlagCatalog.IsAllowlisted(name), name));

    [Fact]
    public void Every_preset_has_a_translated_name_and_description() =>
        Assert.All(FastFlagPresets.All, p =>
        {
            Assert.NotEqual(p.Key, p.Name);
            Assert.NotEqual(p.Key + ".Text", p.Description);
        });
}

public class GameProfileTests
{
    private sealed class FakeRoblox : IRobloxUserSettingsService
    {
        public RobloxUserSettings Stored { get; set; } = new() { FramerateCap = 60, SavedQualityLevel = 5 };
        public bool Refuses { get; set; }
        public bool Exists => true;
        public string FilePath => "x";
        public RobloxUserSettings Load() => Stored;
        public void Save(RobloxUserSettings changes)
        {
            if (Refuses) throw new InvalidOperationException("Roblox is open");
            Stored = Stored with { FramerateCap = changes.FramerateCap ?? Stored.FramerateCap, SavedQualityLevel = changes.SavedQualityLevel ?? Stored.SavedQualityLevel };
        }
        public SettingsBackup BackupNow() => throw new NotSupportedException();
        public IReadOnlyList<SettingsBackup> ListBackups() => [];
        public void Restore(SettingsBackup backup) { }
    }

    private sealed class FakeSession : IRobloxSessionService
    {
        public bool IsActive { get; set; }
        public RobloxActivity? Current => null;
        public DateTime? StartedUtc => null;
        public event EventHandler? Changed { add { } remove { } }
        public void Track(int processId, DateTime launchedAtUtc) { }
        public Task WaitForEndAsync() => Task.CompletedTask;
    }

    private readonly MemorySettings _settings = new();
    private readonly FakeRoblox _roblox = new();
    private readonly FakeSession _session = new();

    private GameProfileService Create() => new(_settings, _roblox, _session, NullLogger<GameProfileService>.Instance);

    [Fact]
    public void Applies_a_profile_and_puts_the_old_values_back()
    {
        var service = Create();
        var profile = service.Add(42, "Game");
        profile.FramerateCap = 240;
        profile.Quality = 2;

        service.ApplyFor(42);
        Assert.Equal(240, _roblox.Stored.FramerateCap);
        Assert.Equal(2, _roblox.Stored.SavedQualityLevel);

        service.RestorePending();
        Assert.Equal(60, _roblox.Stored.FramerateCap);
        Assert.Equal(5, _roblox.Stored.SavedQualityLevel);
        Assert.Null(_settings.Current.Launcher.ProfileRestore);
    }

    [Fact]
    public void Games_without_a_profile_change_nothing()
    {
        var service = Create();

        service.ApplyFor(42);

        Assert.Equal(60, _roblox.Stored.FramerateCap);
        Assert.Null(_settings.Current.Launcher.ProfileRestore);
    }

    [Fact]
    public void A_second_game_doesnt_overwrite_the_values_to_restore()
    {
        var service = Create();
        service.Add(1, "A").FramerateCap = 120;
        service.Add(2, "B").FramerateCap = 240;

        service.ApplyFor(1);
        service.ApplyFor(2);
        service.RestorePending();

        Assert.Equal(60, _roblox.Stored.FramerateCap);
    }

    [Fact]
    public void Roblox_being_open_doesnt_stop_the_launch_and_keeps_the_restore_waiting()
    {
        var service = Create();
        service.Add(1, "A").FramerateCap = 120;
        service.ApplyFor(1);

        _roblox.Refuses = true;
        service.RestorePending();
        Assert.NotNull(_settings.Current.Launcher.ProfileRestore);

        _roblox.Refuses = false;
        Create(); // the next start finishes the job
        Assert.Equal(60, _roblox.Stored.FramerateCap);
    }

    [Fact]
    public void Doesnt_restore_while_a_session_is_running()
    {
        var service = Create();
        service.Add(1, "A").FramerateCap = 120;
        service.ApplyFor(1);

        _session.IsActive = true;
        service.RestorePending();

        Assert.Equal(120, _roblox.Stored.FramerateCap);
    }

    [Fact]
    public void Adding_the_same_game_twice_keeps_one_profile()
    {
        var service = Create();

        Assert.Same(service.Add(1, "A"), service.Add(1, "A"));
        Assert.Single(service.Profiles);
    }
}
