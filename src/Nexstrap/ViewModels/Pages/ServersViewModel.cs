using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
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
    private long _placeId;
    private string? _nextCursor;

    public ServersViewModel(IServerBrowserService servers, IExperienceInfoService experiences, ILaunchCoordinator launch)
    {
        _servers = servers;
        _experiences = experiences;
        _launch = launch;
        ServersView = CollectionViewSource.GetDefaultView(Servers);
        ServersView.Filter = o => Matches((GameServer)o);
    }

    public override string Title => "Servers";

    public override string Subtitle => "Browse an experience's public servers and join a specific one.";

    public ObservableCollection<GameServer> Servers { get; } = [];

    public ICollectionView ServersView { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
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

    // ===== Filters =====

    public IReadOnlyList<Choice<ServerSort>> SortChoices { get; } =
    [
        new(ServerSort.MostPlayers, "Most players"),
        new(ServerSort.FewestPlayers, "Fewest players"),
        new(ServerSort.LowestPing, "Lowest ping")
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
        Status = "Loading servers…";
        try
        {
            // Full servers are filtered locally so the toggle works without reloading.
            var page = await _servers.GetServersAsync(_placeId, Sort != ServerSort.FewestPlayers, excludeFull: false, _nextCursor);
            var known = Servers.Select(s => s.Id).ToHashSet();
            foreach (var server in page.Servers.Where(s => known.Add(s.Id))) Servers.Add(server);
            _nextCursor = page.NextCursor;
            HasMore = page.NextCursor is not null;
            Status = Servers.Count == 0 ? "No public servers are running right now." : $"{Servers.Count} servers loaded.";
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
        if (result is { Success: true }) Status = "Joining that server…";
    }
}
