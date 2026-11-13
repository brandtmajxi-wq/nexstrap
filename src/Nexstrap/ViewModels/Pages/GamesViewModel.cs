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

    /// <summary>Your own note on this game, saved in Settings.json.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNote))]
    private string _note = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNote))]
    private bool _isEditingNote;

    public bool ShowsNote => Note.Length > 0 && !IsEditingNote;

    /// <summary>Set once the saved note is filled in, so loading it doesn't count as a change.</summary>
    public Action<long, string>? SaveNote { get; set; }

    /// <summary>A note is saved when its box closes (or the page is left), not on every key.</summary>
    partial void OnIsEditingNoteChanged(bool value)
    {
        if (!value) SaveNote?.Invoke(PlaceId, Note);
    }
}

/// <summary>One line of the play history list.</summary>
/// <summary>A row in Play history. Share is this game's time next to the most played one (0 to 1), for the bar.</summary>
public sealed record HistoryRow(string Name, string TimeText, string LastPlayedText, double Share = 0);

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

    private readonly IFileDialogService _files;

    public GamesViewModel(ISettingsService settings, ILaunchCoordinator launch, IExperienceInfoService experiences,
        IGameShortcutService shortcuts, IDialogService dialogs, INavigationService navigation, ServersViewModel servers, IPlayHistoryService history, IGameSearchService search, IGameProfileService profiles,
        IFileDialogService files)
    {
        _files = files;
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
            var game = Tile(result.PlaceId, result.Name, favorites.Contains(result.PlaceId));
            game.Creator = result.CreatorName.Length > 0 ? result.CreatorName : null;
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

    /// <summary>Saves the sessions and the per-game totals as two CSV files side by side.</summary>
    [RelayCommand]
    private void ExportHistory()
    {
        var path = _files.SaveFile(Loc.Get("Games.ExportHistory.Title"), "CSV (*.csv)|*.csv", Loc.Get("Games.ExportHistory.FileName") + ".csv");
        if (path is null) return;

        var gamesPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "",
            System.IO.Path.GetFileNameWithoutExtension(path) + Loc.Get("Games.ExportHistory.GamesSuffix") + ".csv");
        var games = _history.Games;
        var names = games.Where(g => g.Name.Length > 0).GroupBy(g => g.PlaceId).ToDictionary(g => g.Key, g => g.First().Name);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var utf8 = new System.Text.UTF8Encoding(true); // the BOM tells Excel it's UTF-8, so names keep their letters
        try
        {
            System.IO.File.WriteAllText(path, HistoryExport.Sessions(_history.Sessions, names, culture), utf8);
            System.IO.File.WriteAllText(gamesPath, HistoryExport.Games(games, culture), utf8);
            _dialogs.Show(Loc.Get("Games.ExportHistory.Done"), Loc.Get("Games.ExportHistory.DoneText"), DialogKind.Info,
                path + Environment.NewLine + gamesPath);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("Games.ExportHistory.Failed"), ex.Message, DialogKind.Error);
        }
    }

    /// <summary>The new look: games as a grid of picture cards and bars in Play history. DEV only for now.</summary>
    public bool UseNewLook => NewLook.Games;

    private void RebuildHistory()
    {
        History.Clear();
        var games = PlayStats.MergeByName(_history.Games).Take(HistoryRows).ToList();
        var most = Math.Max(games.FirstOrDefault()?.Seconds ?? 1, 1);
        foreach (var game in games)
        {
            var name = game.Name.Length > 0 ? game.Name : Loc.Format("Launch.Place", game.PlaceId);
            History.Add(new HistoryRow(name, PlayHistoryService.FormatDuration(game.Seconds),
                Loc.Format("Games.LastPlayed", game.LastPlayedUtc.ToLocalTime()), Math.Clamp(game.Seconds / most, 0, 1)));
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

    // ===== Notes =====

    internal const int NoteLength = 200;

    private GameItem Tile(long placeId, string name, bool favorite)
    {
        var game = new GameItem(placeId, name, favorite)
        {
            Note = _settings.Current.Launcher.GameNotes.GetValueOrDefault(placeId) ?? ""
        };
        game.SaveNote = SaveNote;
        return game;
    }

    /// <summary>Saves a note, trimmed and at most 200 characters; an empty note is removed. Every tile of that game shows it.</summary>
    private void SaveNote(long placeId, string note)
    {
        note = Tidy(note);
        var notes = _settings.Current.Launcher.GameNotes;
        if (notes.GetValueOrDefault(placeId) == (note.Length > 0 ? note : null)) return;
        if (note.Length > 0) notes[placeId] = note;
        else notes.Remove(placeId);
        _settings.Save();
        foreach (var other in Favorites.Concat(Recent).Concat(SearchResults).Where(g => g.PlaceId == placeId && g.Note != note))
            other.Note = note;
    }

    public override void OnNavigatedFrom()
    {
        foreach (var game in Favorites.Concat(Recent).Concat(SearchResults).Where(g => g.IsEditingNote))
            game.IsEditingNote = false;
    }

    internal static string Tidy(string note)
    {
        note = string.Join(' ', note.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return note.Length > NoteLength ? note[..NoteLength].TrimEnd() : note;
    }

    [RelayCommand]
    private void EditNote(GameItem game) => game.IsEditingNote = !game.IsEditingNote;

    // ===== Surprise me =====

    public bool CanSurprise => HasFavorites || HasRecent;

    /// <summary>Picks a game from the favorites and recent games, never the same one twice in a row when there's a choice.</summary>
    internal static long Pick(IReadOnlyList<long> places, long? avoid, Random random)
    {
        var choices = places.Count > 1 && avoid is { } skip ? places.Where(p => p != skip).ToList() : places.ToList();
        return choices[random.Next(choices.Count)];
    }

    private long? _lastSurprise;

    [RelayCommand]
    private async Task SurpriseAsync()
    {
        var games = Favorites.Concat(Recent).GroupBy(g => g.PlaceId).Select(g => g.First()).ToList();
        if (games.Count == 0)
        {
            Status = Loc.Get("Games.Surprise.Empty");
            return;
        }

        var places = games.Select(g => g.PlaceId).ToList();
        while (true)
        {
            var place = Pick(places, _lastSurprise, Random.Shared);
            _lastSurprise = place;
            var game = games.First(g => g.PlaceId == place);
            var choice = _dialogs.Choose(Loc.Get("Games.Surprise.Title"), Loc.Format("Games.Surprise.Text", game.Name),
                Loc.Get("Games.Play"), places.Count > 1 ? Loc.Get("Games.Surprise.Another") : null, Loc.Get("Common.Cancel"));
            if (choice == DialogChoice.Confirm)
            {
                await PlayAsync(game);
                return;
            }
            if (choice != DialogChoice.Extra) return;
        }
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
        foreach (var f in StoredFavorites) Favorites.Add(Tile(f.PlaceId, f.Name, true));

        Recent.Clear();
        foreach (var r in _settings.Current.Launcher.RecentExperiences)
            Recent.Add(Tile(r.PlaceId, r.Name, favorites.Contains(r.PlaceId)));

        foreach (var game in Favorites.Concat(Recent)) ShowPlayTime(game);
        foreach (var game in SearchResults) game.IsFavorite = favorites.Contains(game.PlaceId);
        RebuildHistory();

        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(CanSurprise));
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
