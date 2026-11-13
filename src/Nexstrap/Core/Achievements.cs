using Nexstrap.Services.Roblox;

namespace Nexstrap.Core;

/// <summary>What the achievements are worked out from. Everything comes from History.json on this PC.</summary>
public sealed record PlayFacts(
    double TotalSeconds,
    int Sessions,
    int Games,
    int BestStreak,
    double LongestSessionSeconds,
    double MostInOneGameSeconds,
    bool PlayedLateAtNight,
    bool PlayedEarlyMorning)
{
    public static PlayFacts From(IReadOnlyList<GamePlayStats> games, IReadOnlyList<PlaySession> sessions, DateTime todayLocal)
    {
        var merged = PlayStats.MergeByName(games);
        var startHours = sessions.Select(s => DateTime.SpecifyKind(s.StartedUtc, DateTimeKind.Utc).ToLocalTime().Hour).ToList();
        return new PlayFacts(
            games.Sum(g => g.Seconds),
            games.Sum(g => g.Sessions),
            merged.Count,
            PlayStats.Streaks(sessions, todayLocal).Best,
            sessions.Count == 0 ? 0 : sessions.Max(s => s.Seconds),
            merged.Count == 0 ? 0 : merged.Max(g => g.Seconds),
            startHours.Any(h => h is >= 0 and < 5),
            startHours.Any(h => h is >= 5 and < 8));
    }
}

/// <summary>One achievement: how far along it is (Value of Goal) and whether it's done.</summary>
public sealed record AchievementState(string Id, string Glyph, double Value, double Goal)
{
    public bool Unlocked => Value >= Goal;

    public double Progress => Goal <= 0 ? 1 : Math.Clamp(Value / Goal, 0, 1);
}

/// <summary>
/// Small milestones on the Stats page. They're only for fun, are worked out from your play history
/// each time, and go away if the history is cleared. Nothing is sent anywhere.
/// </summary>
public static class Achievements
{
    private const double Hour = 3600;

    private sealed record Rule(string Id, string Glyph, Func<PlayFacts, double> Value, double Goal);

    private static readonly Rule[] Rules =
    [
        new("FirstGame", "", f => f.Sessions, 1),
        new("Hours1", "", f => f.TotalSeconds / Hour, 1),
        new("Hours10", "", f => f.TotalSeconds / Hour, 10),
        new("Hours50", "", f => f.TotalSeconds / Hour, 50),
        new("Hours100", "", f => f.TotalSeconds / Hour, 100),
        new("Sessions100", "", f => f.Sessions, 100),
        new("Games5", "", f => f.Games, 5),
        new("Games25", "", f => f.Games, 25),
        new("Streak3", "", f => f.BestStreak, 3),
        new("Streak7", "", f => f.BestStreak, 7),
        new("Streak30", "", f => f.BestStreak, 30),
        new("Marathon", "", f => f.LongestSessionSeconds / Hour, 2),
        new("Loyal", "", f => f.MostInOneGameSeconds / Hour, 10),
        new("NightOwl", "", f => f.PlayedLateAtNight ? 1 : 0, 1),
        new("EarlyBird", "", f => f.PlayedEarlyMorning ? 1 : 0, 1)
    ];

    public static int Count => Rules.Length;

    /// <summary>Every achievement in a fixed order: unlocked ones first, then the closest to unlocking.</summary>
    public static IReadOnlyList<AchievementState> Evaluate(PlayFacts facts) =>
        Rules.Select(r => new AchievementState(r.Id, r.Glyph, r.Value(facts), r.Goal))
            .OrderByDescending(a => a.Unlocked)
            .ThenByDescending(a => a.Unlocked ? 0 : a.Progress)
            .ToList();
}
