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
}

public sealed partial class GamesViewModel : PageViewModel
{
    private readonly ISettingsService _settings;
    private readonly ILaunchCoordinator _launch;
    private readonly IExperienceInfoService _experiences;
    private readonly IGameShortcutService _shortcuts;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ServersViewModel _servers;

    public GamesViewModel(ISettingsService settings, ILaunchCoordinator launch, IExperienceInfoService experiences,
        IGameShortcutService shortcuts, IDialogService dialogs, INavigationService navigation, ServersViewModel servers)
    {
        _settings = settings;
        _launch = launch;
        _experiences = experiences;
        _shortcuts = shortcuts;
        _dialogs = dialogs;
        _navigation = navigation;
        _servers = servers;
        launch.RecentsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Rebuild);
    }

    public override string Title => Loc.Get("Nav.Games");

    public override string Subtitle => Loc.Get("Page.Games");

    public ObservableCollection<GameItem> Favorites { get; } = [];

    public ObservableCollection<GameItem> Recent { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    public bool HasRecent => Recent.Count > 0;

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
