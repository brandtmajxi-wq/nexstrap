using Nexstrap.Core;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class AchievementsTests
{
    private static readonly DateTime Today = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Local);

    private static PlaySession At(DateTime local, double minutes, long place = 1) =>
        new() { PlaceId = place, StartedUtc = local.ToUniversalTime(), Seconds = minutes * 60 };

    [Fact]
    public void Nothing_is_unlocked_without_history()
    {
        var all = Achievements.Evaluate(PlayFacts.From([], [], Today));
        Assert.Equal(Achievements.Count, all.Count);
        Assert.DoesNotContain(all, a => a.Unlocked);
        Assert.All(all, a => Assert.InRange(a.Progress, 0, 1));
    }

    [Fact]
    public void A_first_short_game_unlocks_first_steps_only()
    {
        var sessions = new[] { At(Today.AddHours(-1), 20) };
        var games = new[] { new GamePlayStats { PlaceId = 1, Name = "Obby", Seconds = 1200, Sessions = 1 } };
        var all = Achievements.Evaluate(PlayFacts.From(games, sessions, Today));
        Assert.Equal(["FirstGame"], all.Where(a => a.Unlocked).Select(a => a.Id));
        Assert.Equal("FirstGame", all[0].Id);
    }

    [Fact]
    public void Hours_streaks_and_long_sessions_add_up()
    {
        var sessions = Enumerable.Range(0, 7).Select(d => At(Today.Date.AddDays(-d).AddHours(14), 130, d % 2 + 1)).ToList();
        var games = new[]
        {
            new GamePlayStats { PlaceId = 1, Name = "Obby", Seconds = 4 * 130 * 60, Sessions = 4 },
            new GamePlayStats { PlaceId = 2, Name = "Tycoon", Seconds = 3 * 130 * 60, Sessions = 3 }
        };
        var unlocked = Achievements.Evaluate(PlayFacts.From(games, sessions, Today)).Where(a => a.Unlocked).Select(a => a.Id).ToHashSet();
        Assert.Contains("Hours10", unlocked);
        Assert.Contains("Streak7", unlocked);
        Assert.Contains("Marathon", unlocked);
        Assert.DoesNotContain("Hours50", unlocked);
        Assert.DoesNotContain("Streak30", unlocked);
        Assert.DoesNotContain("Games5", unlocked);
        Assert.DoesNotContain("NightOwl", unlocked);
    }

    [Fact]
    public void Late_and_early_starts_count_by_local_time()
    {
        var facts = PlayFacts.From([], [At(Today.Date.AddHours(2), 10), At(Today.Date.AddHours(6), 10)], Today);
        Assert.True(facts.PlayedLateAtNight);
        Assert.True(facts.PlayedEarlyMorning);
        facts = PlayFacts.From([], [At(Today.Date.AddHours(12), 10)], Today);
        Assert.False(facts.PlayedLateAtNight || facts.PlayedEarlyMorning);
    }

    [Fact]
    public void Places_of_the_same_game_count_as_one_game()
    {
        var games = new[]
        {
            new GamePlayStats { PlaceId = 1, Name = "Adopt", Seconds = 3600 * 6 },
            new GamePlayStats { PlaceId = 2, Name = "Adopt", Seconds = 3600 * 5 }
        };
        var facts = PlayFacts.From(games, [], Today);
        Assert.Equal(1, facts.Games);
        Assert.True(Achievements.Evaluate(facts).Single(a => a.Id == "Loyal").Unlocked);
    }

    [Fact]
    public void Play_time_is_split_across_the_hours_and_days_it_covers()
    {
        // Saturday 22:30 for 2 hours: 30 min at 22, 60 at 23, 30 at 0 on Sunday.
        var saturday = new DateTime(2026, 11, 7, 22, 30, 0, DateTimeKind.Local);
        var (hours, days) = PlayStats.Rhythm([At(saturday, 120)]);
        Assert.Equal(1800, hours[22], 3);
        Assert.Equal(3600, hours[23], 3);
        Assert.Equal(1800, hours[0], 3);
        Assert.Equal(5400, days[(int)DayOfWeek.Saturday], 3);
        Assert.Equal(1800, days[(int)DayOfWeek.Sunday], 3);
        Assert.Equal(7200, hours.Sum(), 3);
    }

    [Fact]
    public void No_sessions_means_an_empty_rhythm()
    {
        var (hours, days) = PlayStats.Rhythm([]);
        Assert.Equal(24, hours.Length);
        Assert.Equal(7, days.Length);
        Assert.All(hours.Concat(days), v => Assert.Equal(0, v));
    }

    [Fact]
    public void Every_achievement_has_a_name_and_text_in_english()
    {
        var keys = Loc.Load(Loc.DefaultCode);
        foreach (var a in Achievements.Evaluate(PlayFacts.From([], [], Today)))
        {
            Assert.True(keys.ContainsKey("Achievement." + a.Id), a.Id);
            Assert.True(keys.ContainsKey("Achievement." + a.Id + ".Text"), a.Id);
        }
    }
}
