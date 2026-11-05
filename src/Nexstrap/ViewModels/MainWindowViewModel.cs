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
            new("Home", "", typeof(HomeViewModel)),
            new("Games", "", typeof(GamesViewModel)),
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
            if (Application.Current is { } app) app.Dispatcher.BeginInvoke(ArrangeSidebar);
            else ArrangeSidebar();
        };

        updates.Checked += (_, result) => Application.Current?.Dispatcher.BeginInvoke(() =>
            UpdateBanner = result.Status == UpdateStatus.UpdateAvailable && !_bannerDismissed ? result.Message : null);
    }

    public INavigationService Navigation { get; }

    /// <summary>The pages the sidebar shows, in the chosen order.</summary>
    public ObservableCollection<NavItem> NavItems { get; } = [];

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

    public string VersionLabel => $"v{AppInfo.VersionString}";

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
