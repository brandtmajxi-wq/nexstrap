using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexstrap.Core;
using Nexstrap.Services.Roblox;

namespace Nexstrap.ViewModels.Pages;

public sealed record StatBar(string Label, double Height, string Tip);

public sealed record TopGame(string Name, string Time, double Share);

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

    public override void OnNavigatedTo() => Refresh();

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
            Days.Add(new StatBar(date.ToString("dd"), height, $"{date:d}: {PlayHistoryService.FormatDuration(day.Seconds)}"));
        }

        var games = PlayStats.MergeByName(_history.Games).Take(TopCount).ToList();
        var most = Math.Max(games.FirstOrDefault()?.Seconds ?? 1, 1);
        TopGames.Clear();
        foreach (var game in games)
            TopGames.Add(new TopGame(game.Name.Length > 0 ? game.Name : Loc.Format("Launch.Place", game.PlaceId),
                PlayHistoryService.FormatDuration(game.Seconds), game.Seconds / most * 100));
    }
}
