using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexstrap.Core;
using Nexstrap.Services.Roblox;

namespace Nexstrap.ViewModels.Pages;

/// <summary>One bar. Highlight marks the best day, busiest hour or busiest day, which the new look shows brighter.</summary>
public sealed record StatBar(string Label, double Height, string Tip, bool Highlight = false);

/// <summary>A top game; Rank 1 to 3 get a medal in the new look.</summary>
public sealed record TopGame(string Name, string Time, double Share, int Rank = 0)
{
    /// <summary>The rank number; the page colours 1 to 3 gold, silver and bronze (WPF draws emoji in black and white).</summary>
    public string Medal => Rank.ToString();
}

public sealed record AchievementCard(string Name, string Description, string Glyph, bool Unlocked, double Percent, string ProgressText)
{
    public bool ShowsProgress => !Unlocked && ProgressText.Length > 0;
}

public sealed partial class StatsViewModel : PageViewModel
{
    private const int ChartDays = 14;
    private const double ChartHeight = 120;
    private const int TopCount = 5;

    private readonly IPlayHistoryService _history;

    public StatsViewModel(IPlayHistoryService history)
    {
        _history = history;
        history.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    public override string Title => Loc.Get("Nav.Stats");

    public override string Subtitle => Loc.Get("Page.Stats");

    public ObservableCollection<StatBar> Days { get; } = [];

    public ObservableCollection<TopGame> TopGames { get; } = [];

    public ObservableCollection<AchievementCard> AchievementList { get; } = [];

    public ObservableCollection<StatBar> HourBars { get; } = [];

    public ObservableCollection<StatBar> DayBars { get; } = [];

    [ObservableProperty]
    private string _busiest = "";

    [ObservableProperty]
    private string _achievementSummary = "";

    [ObservableProperty]
    private string _totalTime = "";

    [ObservableProperty]
    private string _totalSessions = "";

    [ObservableProperty]
    private string _currentStreak = "";

    [ObservableProperty]
    private string _bestStreak = "";

    [ObservableProperty]
    private bool _hasData;

    /// <summary>The new look (icons, highlights, medals, extra numbers). DEV only for now.</summary>
    public bool UseNewLook => NewLook.Stats;

    [ObservableProperty]
    private string _averageSession = "";

    [ObservableProperty]
    private string _longestSession = "";

    [ObservableProperty]
    private string _differentGames = "";

    public override void OnNavigatedTo() => Refresh();

    private const double RhythmHeight = 80;

    /// <summary>Play time by hour of the day and by weekday, with the week starting on the day Windows uses.</summary>
    private void RefreshRhythm(IReadOnlyList<PlaySession> sessions)
    {
        var (hours, weekdays) = PlayStats.Rhythm(sessions);
        var dayNames = System.Globalization.CultureInfo.GetCultureInfo(Loc.Code).DateTimeFormat;

        static string HourRange(int hour) =>
            $"{DateTime.Today.AddHours(hour):t}–{DateTime.Today.AddHours(hour + 1):t}";

        var mostHour = Math.Max(hours.Max(), 1);
        HourBars.Clear();
        for (var h = 0; h < 24; h++)
            HourBars.Add(new StatBar(h % 3 == 0 ? h.ToString() : "", hours[h] > 0 ? Math.Max(3, hours[h] / mostHour * RhythmHeight) : 2,
                $"{HourRange(h)}: {PlayHistoryService.FormatDuration(hours[h])}", hours[h] > 0 && hours[h] == hours.Max()));

        var first = (int)System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var mostDay = Math.Max(weekdays.Max(), 1);
        DayBars.Clear();
        for (var i = 0; i < 7; i++)
        {
            var d = (first + i) % 7;
            DayBars.Add(new StatBar(dayNames.AbbreviatedDayNames[d], weekdays[d] > 0 ? Math.Max(3, weekdays[d] / mostDay * RhythmHeight) : 2,
                $"{dayNames.DayNames[d]}: {PlayHistoryService.FormatDuration(weekdays[d])}", weekdays[d] > 0 && weekdays[d] == weekdays.Max()));
        }

        Busiest = hours.Max() > 0
            ? Loc.Format("Stats.Busiest", HourRange(Array.IndexOf(hours, hours.Max())), dayNames.DayNames[Array.IndexOf(weekdays, weekdays.Max())])
            : "";
    }

    private void Refresh()
    {
        var sessions = _history.Sessions;
        var now = DateTime.Now;

        HasData = _history.TotalSessions > 0;
        TotalTime = PlayHistoryService.FormatDuration(_history.TotalSeconds);
        TotalSessions = _history.TotalSessions.ToString("N0");
        var (current, best) = PlayStats.Streaks(sessions, now);
        CurrentStreak = Loc.Format("Stats.DaysValue", current);
        BestStreak = Loc.Format("Stats.DaysValue", best);

        var days = PlayStats.LastDays(sessions, now, ChartDays);
        var longest = Math.Max(days.Max(d => d.Seconds), 1);
        Days.Clear();
        foreach (var day in days)
        {
            var date = day.Day.ToDateTime(TimeOnly.MinValue);
            var height = day.Seconds > 0 ? Math.Max(4, day.Seconds / longest * ChartHeight) : 2;
            Days.Add(new StatBar(date.ToString("dd"), height, $"{date:d}: {PlayHistoryService.FormatDuration(day.Seconds)}", day.Seconds > 0 && day.Seconds >= longest));
        }

        var games = PlayStats.MergeByName(_history.Games).Take(TopCount).ToList();
        var most = Math.Max(games.FirstOrDefault()?.Seconds ?? 1, 1);
        TopGames.Clear();
        var rank = 0;
        foreach (var game in games)
            TopGames.Add(new TopGame(game.Name.Length > 0 ? game.Name : Loc.Format("Launch.Place", game.PlaceId),
                PlayHistoryService.FormatDuration(game.Seconds), game.Seconds / most * 100, ++rank));

        var counted = sessions.Where(s => s.Seconds > 0).ToList();
        AverageSession = counted.Count > 0 ? PlayHistoryService.FormatDuration(counted.Average(s => s.Seconds)) : "–";
        LongestSession = counted.Count > 0 ? PlayHistoryService.FormatDuration(counted.Max(s => s.Seconds)) : "–";
        DifferentGames = PlayStats.MergeByName(_history.Games).Count().ToString("N0");

        RefreshRhythm(sessions);

        var achievements = Achievements.Evaluate(PlayFacts.From(_history.Games, sessions, now));
        AchievementSummary = Loc.Format("Stats.AchievementsCount", achievements.Count(a => a.Unlocked), Achievements.Count);
        AchievementList.Clear();
        foreach (var a in achievements)
            AchievementList.Add(new AchievementCard(
                Loc.GetOr("Achievement." + a.Id, a.Id),
                Loc.GetOr("Achievement." + a.Id + ".Text", ""),
                a.Glyph,
                a.Unlocked,
                a.Progress * 100,
                a.Goal > 1 ? $"{Math.Floor(Math.Min(a.Value, a.Goal)):N0} / {a.Goal:N0}" : ""));
    }
}
