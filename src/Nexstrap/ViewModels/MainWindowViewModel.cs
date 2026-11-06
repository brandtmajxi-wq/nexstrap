using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Notifications;
using Nexstrap.Services.Updates;
using Nexstrap.ViewModels.Pages;

namespace Nexstrap.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>The launch-time check must never hold anything up.</summary>
    private static readonly TimeSpan StartupCheckTimeout = TimeSpan.FromSeconds(6);

    private readonly IUpdateService _updates;
    private readonly ISettingsService _settings;
    private readonly INotificationCenter _notifications;

    /// <summary>Every page, including hidden ones: links inside the app can still open them.</summary>
    private readonly IReadOnlyList<NavItem> _pages;

    public MainWindowViewModel(INavigationService navigation, IUpdateService updates, ISettingsService settings, INotificationCenter notifications)
    {
        Navigation = navigation;
        _updates = updates;
        _settings = settings;
        _notifications = notifications;
        _pages =
        [
            new("Welcome", char.ConvertFromUtf32(0xE823), typeof(WelcomeViewModel)),
            new("Home", "", typeof(HomeViewModel)),
            new("Games", "", typeof(GamesViewModel)),
            new("Stats", char.ConvertFromUtf32(0xE9D2), typeof(StatsViewModel)),
            new("Servers", "", typeof(ServersViewModel)),
            new("Appearance", "", typeof(AppearanceViewModel)),
            new("Loading Screen", "", typeof(LaunchScreenViewModel)),
            new("Performance", "", typeof(PerformanceViewModel)),
            new("FastFlags", "", typeof(FastFlagsViewModel)),
            new("Roblox Settings", "", typeof(RobloxSettingsViewModel)),
            new("Integrations", "", typeof(IntegrationsViewModel)),
        ];
        var inbox = new NavItem("Notifications", "", typeof(NotificationsViewModel));
        FooterItems = [inbox, new("About", "", typeof(AboutViewModel))];

        notifications.Changed += (_, _) =>
        {
            var count = notifications.Active.Count;
            if (Application.Current is { } app) app.Dispatcher.BeginInvoke(() => inbox.Badge = count);
            else inbox.Badge = count;
        };

        // Selection drives navigation, so mouse, keyboard (arrow keys) and UI Automation all work.
        foreach (var item in _pages.Concat(FooterItems))
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NavItem.IsSelected) && item.IsSelected)
                    Navigation.NavigateTo(item.PageType);
            };
        }

        navigation.Navigated += (_, page) =>
        {
            foreach (var item in _pages.Concat(FooterItems))
                item.IsSelected = item.PageType == page.GetType();
        };

        ArrangeSidebar();
        settings.Saved += (_, _) =>
        {
            if (Application.Current is { } app) app.Dispatcher.BeginInvoke(AfterSettingsSaved);
            else AfterSettingsSaved();
        };

        updates.Checked += (_, result) => Application.Current?.Dispatcher.BeginInvoke(() =>
            UpdateBanner = result.Status == UpdateStatus.UpdateAvailable && !_bannerDismissed ? result.Message : null);
    }

    public INavigationService Navigation { get; }

    // ===== Settings search =====

    public ObservableCollection<SettingResult> SearchResults { get; } = [];

    public bool HasSearchResults => SearchResults.Count > 0;

    [ObservableProperty]
    private string _settingsQuery = "";

    [RelayCommand]
    private void ClearSettingsSearch() => SettingsQuery = "";

    /// <summary>Asks the window to scroll to the card with this header.</summary>
    public event EventHandler<string>? ScrollToSetting;

    partial void OnSettingsQueryChanged(string value)
    {
        SearchResults.Clear();
        foreach (var result in SettingsCatalog.Search(value)) SearchResults.Add(result);
        OnPropertyChanged(nameof(HasSearchResults));
    }

    [RelayCommand]
    private void OpenSearchResult(SettingResult result)
    {
        SettingsQuery = "";
        if (_pages.Concat(FooterItems).FirstOrDefault(p => p.Title == result.Page) is not { } page) return;
        Navigation.NavigateTo(page.PageType);
        ScrollToSetting?.Invoke(this, result.Label);
    }

    /// <summary>The pages the sidebar shows, in the chosen order.</summary>
    public ObservableCollection<NavItem> NavItems { get; } = [];

    private void AfterSettingsSaved()
    {
        ArrangeSidebar();
        OnPropertyChanged(nameof(ShowSnow));
        OnPropertyChanged(nameof(BrandName));
        OnPropertyChanged(nameof(BrandLogo));
        OnPropertyChanged(nameof(HasBrandLogo));
        OnPropertyChanged(nameof(ShowTagline));
    }

    public bool ShowSnow => _settings.Current.Winter.Snow && WinterSeason.Resolve(_settings.Current.Winter, DateTime.Now, WinterSeason.Available).Winter;

    private void ArrangeSidebar()
    {
        var wanted = SidebarLayout.Visible(_settings.Current.Sidebar);
        if (NavItems.Select(i => i.Title).SequenceEqual(wanted)) return;

        NavItems.Clear();
        foreach (var title in wanted) NavItems.Add(_pages.Single(p => p.Title == title));
    }

    /// <summary>Pinned to the bottom of the sidebar.</summary>
    public IReadOnlyList<NavItem> FooterItems { get; }

    public string AppName => AppInfo.Name;

    public string BrandName => _settings.Current.Appearance.BrandName ?? AppName;

    public bool ShowTagline => !_settings.Current.Appearance.HideTagline && !_settings.Current.Appearance.CompactSidebar;

    public bool HasBrandLogo => BrandLogo is not null;

    private string? _brandLogoPath;
    private System.Windows.Media.ImageSource? _brandLogo;

    /// <summary>Read fully into memory so the file is never locked; a missing file falls back to the Nexstrap logo.</summary>
    public System.Windows.Media.ImageSource? BrandLogo
    {
        get
        {
            var path = _settings.Current.Appearance.BrandLogo;
            if (path == _brandLogoPath) return _brandLogo;
            _brandLogoPath = path;
            _brandLogo = null;
            if (path is null || !System.IO.File.Exists(path)) return null;
            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96;
                image.EndInit();
                image.Freeze();
                _brandLogo = image;
            }
            catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or UriFormatException) { }
            return _brandLogo;
        }
    }

    /// <summary>The dev copy shows when it was built, so it's clear whether it has the latest changes.</summary>
    public string VersionLabel => AppInfo.IsDev && Environment.ProcessPath is { } exe
        ? $"v{AppInfo.VersionString} · {System.IO.File.GetLastWriteTime(exe):d MMM HH:mm}"
        : $"v{AppInfo.VersionString}";

    public string? DevLabel => AppInfo.IsDev ? AppInfo.DevLabel : null;

    // ===== Update banner (shown on every page) =====

    private bool _bannerDismissed;

    [ObservableProperty]
    private string? _updateBanner;

    /// <summary>Quick background check each time Nexstrap opens (when enabled in settings).</summary>
    public async Task CheckForUpdatesOnLaunchAsync()
    {
        _notifications.Refresh(); // the badge shouldn't wait for the network
        if (!_settings.Current.Launcher.CheckForUpdatesOnStartup) return;
        using var cts = new CancellationTokenSource(StartupCheckTimeout);
        await _updates.CheckAsync(cts.Token);
    }

    [RelayCommand]
    private void ViewUpdate() => Navigation.ShowUpdates();

    [RelayCommand]
    private void DismissUpdate()
    {
        _bannerDismissed = true;
        UpdateBanner = null;
    }

    /// <summary>Opens the start page, honouring --page &lt;Title&gt; when given.</summary>
    public void NavigateToStartPage(string? requested)
    {
        var target = _pages.Concat(FooterItems).FirstOrDefault(i =>
                         string.Equals(i.Title.Replace(" ", ""), requested?.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                     ?? NavItems[0];
        Navigation.NavigateTo(target.PageType);
    }
}
