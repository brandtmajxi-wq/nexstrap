using Nexstrap.Services.Roblox;

namespace Nexstrap.Core;

public sealed record DayTotal(DateOnly Day, double Seconds);

/// <summary>Works out daily totals and streaks from saved sessions, using the PC's local dates.</summary>
public static class PlayStats
{
    /// <summary>One entry per day, oldest first, ending today. Days without play count as zero.</summary>
    public static IReadOnlyList<DayTotal> LastDays(IEnumerable<PlaySession> sessions, DateTime todayLocal, int count)
    {
        var totals = sessions.GroupBy(LocalDay).ToDictionary(g => g.Key, g => g.Sum(s => s.Seconds));
        var today = DateOnly.FromDateTime(todayLocal);
        return Enumerable.Range(0, count)
            .Select(i => today.AddDays(i - count + 1))
            .Select(day => new DayTotal(day, totals.GetValueOrDefault(day)))
            .ToList();
    }

    /// <summary>Days in a row with play. The current streak survives until a whole day is missed, so it doesn't reset each morning.</summary>
    public static (int Current, int Best) Streaks(IEnumerable<PlaySession> sessions, DateTime todayLocal)
    {
        var days = sessions.Select(LocalDay).Distinct().Order().ToList();
        if (days.Count == 0) return (0, 0);

        var best = 1;
        var run = 1;
        for (var i = 1; i < days.Count; i++)
        {
            run = days[i] == days[i - 1].AddDays(1) ? run + 1 : 1;
            best = Math.Max(best, run);
        }

        var today = DateOnly.FromDateTime(todayLocal);
        return (days[^1] >= today.AddDays(-1) ? run : 0, best);
    }

    /// <summary>A game with several places shows up once, because players think of it as one game.</summary>
    public static IReadOnlyList<GamePlayStats> MergeByName(IEnumerable<GamePlayStats> games) =>
        games.GroupBy(g => g.Name.Length > 0 ? g.Name : "#" + g.PlaceId)
            .Select(g => new GamePlayStats
            {
                PlaceId = g.OrderByDescending(x => x.Seconds).First().PlaceId,
                Name = g.First().Name,
                Seconds = g.Sum(x => x.Seconds),
                Sessions = g.Sum(x => x.Sessions),
                LastPlayedUtc = g.Max(x => x.LastPlayedUtc)
            })
            .OrderByDescending(g => g.Seconds)
            .ToList();

    /// <summary>
    /// Seconds played in each hour of the day (index 0–23) and each day of the week (index = DayOfWeek, Sunday = 0),
    /// by the PC's local time. A session that runs past the hour is split across the hours it covers.
    /// </summary>
    public static (double[] Hours, double[] Weekdays) Rhythm(IEnumerable<PlaySession> sessions)
    {
        var hours = new double[24];
        var days = new double[7];
        foreach (var session in sessions)
        {
            var start = DateTime.SpecifyKind(session.StartedUtc, DateTimeKind.Utc).ToLocalTime();
            var end = start.AddSeconds(Math.Max(0, session.Seconds));
            for (var t = start; t < end;)
            {
                var next = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind).AddHours(1);
                if (next > end) next = end;
                var seconds = (next - t).TotalSeconds;
                hours[t.Hour] += seconds;
                days[(int)t.DayOfWeek] += seconds;
                t = next;
            }
        }
        return (hours, days);
    }

    private static DateOnly LocalDay(PlaySession session) =>
        DateOnly.FromDateTime(DateTime.SpecifyKind(session.StartedUtc, DateTimeKind.Utc).ToLocalTime());
}
