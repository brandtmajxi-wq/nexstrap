using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Roblox;

namespace Nexstrap.ViewModels.Pages;

public enum ServerSort
{
    MostPlayers,
    FewestPlayers,
    LowestPing
}

public sealed partial class ServersViewModel : PageViewModel
{
    private readonly IServerBrowserService _servers;
    private readonly IExperienceInfoService _experiences;
    private readonly ILaunchCoordinator _launch;
    private readonly IServerRegionService _regions;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _regionSearch;
    private long _placeId;
    private string? _nextCursor;

    public ServersViewModel(IServerBrowserService servers, IExperienceInfoService experiences, ILaunchCoordinator launch,
        IServerRegionService regions, IDialogService dialogs)
    {
        _regions = regions;
        _dialogs = dialogs;
        regions.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(CurrentRegionText)));
        _servers = servers;
        _experiences = experiences;
        _launch = launch;
        ServersView = CollectionViewSource.GetDefaultView(Servers);
        ServersView.Filter = o => Matches((GameServer)o);
    }

    public override string Title => Loc.Get("Nav.Servers");

    public override string Subtitle => Loc.Get("Page.Servers");

    public ObservableCollection<GameServer> Servers { get; } = [];

    public ICollectionView ServersView { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand), nameof(FindRegionCommand))]
    private string _input = "";

    [ObservableProperty]
    private string? _experienceName;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand), nameof(LoadMoreCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _hasMore;

    // ===== Region =====

    public IReadOnlyList<string> Regions => RobloxRegions.All;

    [ObservableProperty]
    private string _selectedRegion = RobloxRegions.Frankfurt;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindRegionCommand), nameof(StopRegionSearchCommand))]
    private bool _isFindingRegion;

    [ObservableProperty]
    private string? _regionStatus;

    public string? CurrentRegionText => _regions.CurrentRegion is { } region ? Loc.Format("Servers.CurrentRegion", region) : null;

    private bool CanFindRegion() => !IsFindingRegion && RobloxLinks.TryParsePlaceId(Input, out _);

    [RelayCommand(CanExecute = nameof(CanFindRegion))]
    private async Task FindRegionAsync()
    {
        RobloxLinks.TryParsePlaceId(Input, out var placeId);
        var region = SelectedRegion;
        if (!_dialogs.Confirm(Loc.Format("Servers.ConfirmRegion.Title", region), Loc.Get("Servers.ConfirmRegion.Text"), Loc.Get("Servers.ConfirmRegion.Go")))
            return;

        IsFindingRegion = true;
        using var search = _regionSearch = new CancellationTokenSource();
        var tries = 0;
        var progress = new Progress<RegionSearchProgress>(p =>
        {
            tries = p.Total;
            RegionStatus = p.Joined
                ? Loc.Format("Servers.RegionLanded", p.Attempt, p.Total, p.Landed ?? Loc.Get("Servers.UnknownRegion"))
                : Loc.Format("Servers.RegionTry", p.Attempt, p.Total);
        });

        try
        {
            var name = placeId == _placeId ? ExperienceName : null;
            var outcome = await _regions.FindAsync(placeId, name, region, progress, search.Token);
            RegionStatus = outcome switch
            {
                RegionSearchOutcome.Found => Loc.Format("Servers.RegionFound", region),
                RegionSearchOutcome.NotFound => Loc.Format("Servers.RegionNotFound", region, tries),
                _ => Loc.Get("Servers.RegionFailed")
            };
        }
        catch (OperationCanceledException)
        {
            RegionStatus = Loc.Get("Servers.RegionStopped");
        }
        catch (ServerBrowserException ex)
        {
            RegionStatus = ex.Message;
        }
        finally
        {
            _regionSearch = null;
            IsFindingRegion = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsFindingRegion))]
    private void StopRegionSearch() => _regionSearch?.Cancel();

    // ===== Filters =====

    public IReadOnlyList<Choice<ServerSort>> SortChoices { get; } =
    [
        new(ServerSort.MostPlayers, () => Loc.Get("Servers.MostPlayers")),
        new(ServerSort.FewestPlayers, () => Loc.Get("Servers.FewestPlayers")),
        new(ServerSort.LowestPing, () => Loc.Get("Servers.LowestPing"))
    ];

    [ObservableProperty]
    private ServerSort _sort = ServerSort.MostPlayers;

    [ObservableProperty]
    private bool _hideFull = true;

    /// <summary>0 means no minimum.</summary>
    [ObservableProperty]
    private double _minFreeSlots;

    /// <summary>0 means no limit.</summary>
    [ObservableProperty]
    private double _maxPing;

    partial void OnSortChanged(ServerSort value)
    {
        // Player order comes from Roblox, so a different direction needs a fresh list.
        if (_placeId != 0 && value != ServerSort.LowestPing) _ = LoadFreshAsync();
        else ApplySort();
    }

    partial void OnHideFullChanged(bool value) => ServersView.Refresh();
    partial void OnMinFreeSlotsChanged(double value) => ServersView.Refresh();
    partial void OnMaxPingChanged(double value) => ServersView.Refresh();

    internal bool Matches(GameServer server) => Matches(server, HideFull, (int)MinFreeSlots, (int)MaxPing);

    internal static bool Matches(GameServer server, bool hideFull, int minFree, int maxPing) =>
        (!hideFull || !server.IsFull) && server.FreeSlots >= minFree && (maxPing <= 0 || server.Ping is { } ping && ping <= maxPing);

    private void ApplySort()
    {
        using (ServersView.DeferRefresh())
        {
            ServersView.SortDescriptions.Clear();
            if (Sort == ServerSort.LowestPing)
                ServersView.SortDescriptions.Add(new SortDescription(nameof(GameServer.Ping), ListSortDirection.Ascending));
        }
    }

    // ===== Loading =====

    private bool CanLoad() => !IsLoading && RobloxLinks.TryParsePlaceId(Input, out _);

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task LoadAsync()
    {
        RobloxLinks.TryParsePlaceId(Input, out _placeId);
        ExperienceName = null;
        _ = LookupNameAsync(_placeId);
        return LoadFreshAsync();
    }

    private async Task LoadFreshAsync()
    {
        Servers.Clear();
        _nextCursor = null;
        HasMore = false;
        ApplySort();
        await FetchAsync();
    }

    private bool CanLoadMore() => HasMore && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync() => FetchAsync();

    private async Task FetchAsync()
    {
        IsLoading = true;
        Status = Loc.Get("Servers.LoadingServers");
        try
        {
            // Full servers are filtered locally so the toggle works without reloading.
            var page = await _servers.GetServersAsync(_placeId, Sort != ServerSort.FewestPlayers, excludeFull: false, _nextCursor);
            var known = Servers.Select(s => s.Id).ToHashSet();
            foreach (var server in page.Servers.Where(s => known.Add(s.Id)))
            {
                server.Region = _regions.KnownRegion(server.Id);
                Servers.Add(server);
            }
            _nextCursor = page.NextCursor;
            HasMore = page.NextCursor is not null;
            Status = Servers.Count == 0 ? Loc.Get("Servers.NoPublicServersAreRunningRight") : Loc.Format("Servers.ServersLoaded", Servers.Count);
        }
        catch (ServerBrowserException ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LookupNameAsync(long placeId)
    {
        try
        {
            var info = await _experiences.LookupAsync(placeId);
            if (placeId == _placeId) ExperienceName = info?.Name;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            // The name is optional; the list still works without it.
        }
    }

    [RelayCommand]
    private async Task JoinAsync(GameServer server)
    {
        var result = await _launch.JoinAsync(_placeId, ExperienceName, server.Id);
        if (result is { Success: true }) Status = Loc.Get("Servers.JoiningThatServer");
    }
}
