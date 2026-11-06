using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Roblox;

namespace Nexstrap.ViewModels.Pages;

/// <summary>A game tile; name details and thumbnail fill in after the public lookup.</summary>
public sealed partial class GameItem(long placeId, string name, bool isFavorite) : ObservableObject
{
    public long PlaceId { get; } = placeId;

    [ObservableProperty]
    private string _name = name;

    [ObservableProperty]
    private string? _creator;

    [ObservableProperty]
    private string? _iconUrl;

    [ObservableProperty]
    private bool _isFavorite = isFavorite;

    [ObservableProperty]
    private string? _playedText;
}

/// <summary>One line of the play history list.</summary>
public sealed record HistoryRow(string Name, string TimeText, string LastPlayedText);

/// <summary>One game profile; changes save straight away.</summary>
public sealed class GameProfileRow(GameProfile profile, IGameProfileService service)
{
    public GameProfile Profile { get; } = profile;

    public string Name => Profile.Name;

    public int Fps
    {
        get => Profile.FramerateCap ?? GamesViewModel.NormalSetting;
        set { Profile.FramerateCap = value == GamesViewModel.NormalSetting ? null : value; service.Save(); }
    }

    public int Quality
    {
        get => Profile.Quality ?? GamesViewModel.NormalSetting;
        set { Profile.Quality = value == GamesViewModel.NormalSetting ? null : value; service.Save(); }
    }
}

public sealed partial class GamesViewModel : PageViewModel
{
    internal const int NormalSetting = -1;

    private readonly IGameProfileService _profiles;

    public ObservableCollection<GameProfileRow> ProfileRows { get; } = [];

    public bool HasProfiles => ProfileRows.Count > 0;

    public IReadOnlyList<Choice<int>> FpsChoices { get; } =
    [
        new(NormalSetting, () => Loc.Get("Games.Profile.Default")), new(60, "60 FPS"), new(120, "120 FPS"), new(144, "144 FPS"), new(240, "240 FPS"),
        new(Nexstrap.Services.Performance.PerformancePlanner.NoLimitCap, () => Loc.Get("Performance.NoLimit.Choice"))
    ];

    public IReadOnlyList<Choice<int>> QualityChoices { get; } = BuildQualityChoices();

    private static IReadOnlyList<Choice<int>> BuildQualityChoices()
    {
        var list = new List<Choice<int>> { new(NormalSetting, () => Loc.Get("Games.Profile.Default")), new(0, () => Loc.Get("Performance.Automatic")) };
        for (var level = 1; level <= 10; level++)
        {
            var shown = level;
            list.Add(new Choice<int>(shown, () => Loc.Format("Games.Profile.Quality", shown)));
        }
        return list;
    }

    private void RebuildProfiles()
    {
        ProfileRows.Clear();
        foreach (var profile in _profiles.Profiles) ProfileRows.Add(new GameProfileRow(profile, _profiles));
        OnPropertyChanged(nameof(HasProfiles));
    }

    [RelayCommand]
    private void AddProfile(GameItem game) => _profiles.Add(game.PlaceId, game.Name);

    [RelayCommand]
    private void RemoveProfile(GameProfileRow row) => _profiles.Remove(row.Profile);

    private readonly ISettingsService _settings;
    private readonly ILaunchCoordinator _launch;
    private readonly IExperienceInfoService _experiences;
    private readonly IGameShortcutService _shortcuts;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ServersViewModel _servers;
    private readonly IPlayHistoryService _history;
    private readonly IGameSearchService _search;

    public GamesViewModel(ISettingsService settings, ILaunchCoordinator launch, IExperienceInfoService experiences,
        IGameShortcutService shortcuts, IDialogService dialogs, INavigationService navigation, ServersViewModel servers, IPlayHistoryService history, IGameSearchService search, IGameProfileService profiles)
    {
        _profiles = profiles;
        profiles.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(RebuildProfiles);
        RebuildProfiles();
        _settings = settings;
        _launch = launch;
        _experiences = experiences;
        _shortcuts = shortcuts;
        _dialogs = dialogs;
        _navigation = navigation;
        _servers = servers;
        _history = history;
        _search = search;
        launch.RecentsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Rebuild);
        history.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Rebuild);
    }

    public override string Title => Loc.Get("Nav.Games");

    public override string Subtitle => Loc.Get("Page.Games");

    public ObservableCollection<GameItem> Favorites { get; } = [];

    public ObservableCollection<GameItem> Recent { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    public bool HasRecent => Recent.Count > 0;

    // ===== Search =====

    public ObservableCollection<GameItem> SearchResults { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchText = "";

    [ObservableProperty]
    private string? _searchStatus;

    private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchText);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var query = SearchText.Trim();
        SearchResults.Clear();
        SearchStatus = Loc.Get("Games.Searching");

        IReadOnlyList<GameSearchResult> results;
        try
        {
            results = await _search.SearchAsync(query);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            SearchStatus = Loc.Get("Games.SearchFailed");
            return;
        }

        var favorites = StoredFavorites.Select(f => f.PlaceId).ToHashSet();
        foreach (var result in results)
        {
            var game = new GameItem(result.PlaceId, result.Name, favorites.Contains(result.PlaceId))
            {
                Creator = result.CreatorName.Length > 0 ? result.CreatorName : null
            };
            ShowPlayTime(game);
            game.PlayedText ??= Loc.Format("Format.Playing", result.Playing);
            SearchResults.Add(game);
        }
        SearchStatus = results.Count == 0 ? Loc.Format("Games.NoResults", query) : null;
        _ = FillSearchIconsAsync(SearchResults.Zip(results).ToList());
    }

    private async Task FillSearchIconsAsync(IReadOnlyList<(GameItem Game, GameSearchResult Result)> items)
    {
        foreach (var (game, result) in items)
        {
            try { game.IconUrl = await _experiences.GetIconUrlAsync(result.UniverseId); }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
            {
                return; // the list still works without pictures
            }
        }
    }

    // ===== Play history =====

    private const int HistoryRows = 8;

    public ObservableCollection<HistoryRow> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    public string HistorySummary => Loc.Format("Games.HistorySummary", PlayHistoryService.FormatDuration(_history.TotalSeconds), _history.TotalSessions);

    public string HistoryThisWeek => Loc.Format("Games.ThisWeek", PlayHistoryService.FormatDuration(_history.SecondsSince(DateTime.UtcNow.AddDays(-7))));

    public bool RecordHistory
    {
        get => _settings.Current.Launcher.RecordPlayHistory;
        set
        {
            _settings.Current.Launcher.RecordPlayHistory = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (_dialogs.Confirm(Loc.Get("Games.ClearHistory.Title"), Loc.Get("Games.ClearHistory.Text"), Loc.Get("Games.ClearHistory")))
            _history.Clear();
    }

    private void RebuildHistory()
    {
        History.Clear();
        foreach (var game in PlayStats.MergeByName(_history.Games).Take(HistoryRows))
        {
            var name = game.Name.Length > 0 ? game.Name : Loc.Format("Launch.Place", game.PlaceId);
            History.Add(new HistoryRow(name, PlayHistoryService.FormatDuration(game.Seconds),
                Loc.Format("Games.LastPlayed", game.LastPlayedUtc.ToLocalTime())));
        }
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HistorySummary));
        OnPropertyChanged(nameof(HistoryThisWeek));
    }

    private void ShowPlayTime(GameItem game)
    {
        var seconds = _history.SecondsFor(game.PlaceId);
        game.PlayedText = seconds > 0 ? Loc.Format("Games.PlayedFor", PlayHistoryService.FormatDuration(seconds)) : null;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFavoriteCommand))]
    private string _input = "";

    [ObservableProperty]
    private string? _status;

    private List<RecentExperience> StoredFavorites => _settings.Current.FavoriteExperiences;

    private bool CanAdd() => RobloxLinks.TryParsePlaceId(Input, out _);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddFavoriteAsync()
    {
        RobloxLinks.TryParsePlaceId(Input, out var placeId);
        Input = "";
        string name = Loc.Format("Launch.Place", placeId);
        try
        {
            if (await _experiences.LookupAsync(placeId) is not { } info)
            {
                Status = Loc.Get("Games.NoExperienceFoundWithThatPlace");
                return;
            }
            name = info.Name;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            // Offline: keep the favorite under its place ID.
        }
        SetFavorite(placeId, name, true);
    }

    [RelayCommand]
    private void ToggleFavorite(GameItem game) => SetFavorite(game.PlaceId, game.Name, !game.IsFavorite);

    internal void SetFavorite(long placeId, string name, bool favorite)
    {
        StoredFavorites.RemoveAll(f => f.PlaceId == placeId);
        if (favorite) StoredFavorites.Insert(0, new RecentExperience { PlaceId = placeId, Name = name, LastLaunched = DateTime.Now });
        _settings.Save();
        Rebuild();
    }

    [RelayCommand]
    private async Task PlayAsync(GameItem game)
    {
        if (await _launch.JoinAsync(game.PlaceId, game.Name) is { Success: true }) Status = Loc.Format("Games.Starting", game.Name);
    }

    [RelayCommand]
    private void ViewServers(GameItem game)
    {
        _servers.Input = game.PlaceId.ToString();
        _navigation.NavigateTo<ServersViewModel>();
        if (_servers.LoadCommand.CanExecute(null)) _servers.LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void CreateShortcut(GameItem game)
    {
        try
        {
            Status = Loc.Format("Games.ShortcutCreatedOnYourDesktop", Path.GetFileName(_shortcuts.CreateOnDesktop(game.PlaceId, game.Name)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("Games.CouldntCreateTheShortcut"), ex.Message, DialogKind.Warning);
        }
    }

    [RelayCommand]
    private void RemoveRecent(GameItem game)
    {
        _settings.Current.Launcher.RecentExperiences.RemoveAll(r => r.PlaceId == game.PlaceId);
        _settings.Save();
        Rebuild();
    }

    public override void OnNavigatedTo() => Rebuild();

    private void Rebuild()
    {
        var favorites = StoredFavorites.Select(f => f.PlaceId).ToHashSet();

        Favorites.Clear();
        foreach (var f in StoredFavorites) Favorites.Add(new GameItem(f.PlaceId, f.Name, true));

        Recent.Clear();
        foreach (var r in _settings.Current.Launcher.RecentExperiences)
            Recent.Add(new GameItem(r.PlaceId, r.Name, favorites.Contains(r.PlaceId)));

        foreach (var game in Favorites.Concat(Recent)) ShowPlayTime(game);
        foreach (var game in SearchResults) game.IsFavorite = favorites.Contains(game.PlaceId);
        RebuildHistory();

        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasRecent));
        _ = FillDetailsAsync(Favorites.Concat(Recent).ToList());
    }

    private async Task FillDetailsAsync(IReadOnlyList<GameItem> games)
    {
        foreach (var game in games)
        {
            try
            {
                if (await _experiences.LookupAsync(game.PlaceId) is not { } info) continue;
                game.Name = info.Name;
                game.Creator = info.CreatorName;
                game.IconUrl = await _experiences.GetIconUrlAsync(info.UniverseId);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
            {
                return; // offline: tiles keep their saved names
            }
        }
    }
}
