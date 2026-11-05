using Nexstrap.Services.Navigation;
using Nexstrap.Services.Updates;
using Nexstrap.ViewModels;
using Nexstrap.ViewModels.Pages;

namespace Nexstrap.Tests;

internal sealed class FakeUpdates : IUpdateService
{
    public int Checks { get; private set; }
    public UpdateCheckResult? LastResult { get; private set; }
    public event EventHandler<UpdateCheckResult>? Checked;

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        Checks++;
        LastResult = new UpdateCheckResult(UpdateStatus.UpToDate, "ok");
        Checked?.Invoke(this, LastResult);
        return Task.FromResult(LastResult);
    }
}

public class NavigationTests
{
    private sealed class FakeNavigation : INavigationService
    {
        public PageViewModel? CurrentPage { get; private set; }
        public event EventHandler<PageViewModel>? Navigated;
        public List<Type> History { get; } = [];

        public void NavigateTo<TPage>() where TPage : PageViewModel => NavigateTo(typeof(TPage));

        public void ShowUpdates() => NavigateTo<HomeViewModel>();

        public void NavigateTo(Type pageType)
        {
            if (CurrentPage?.GetType() == pageType) return;
            History.Add(pageType);
            // Pages take services; navigation only cares about their type.
            CurrentPage = (PageViewModel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(pageType);
            Navigated?.Invoke(this, CurrentPage);
        }
    }

    private sealed class FakeCenter : Nexstrap.Services.Notifications.INotificationCenter
    {
        public IReadOnlyList<Nexstrap.Services.Notifications.AppNotification> Active { get; set; } = [];
        public event EventHandler? Changed;
        public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dismiss(Nexstrap.Services.Notifications.AppNotification notification) { }
        public void DismissAll() { }
        public void RestoreDismissed() { }
    }

    private static MainWindowViewModel Create(FakeNavigation nav, FakeUpdates? updates = null, MemorySettings? settings = null, FakeCenter? center = null) =>
        new(nav, updates ?? new FakeUpdates(), settings ?? new MemorySettings(), center ?? new FakeCenter());

    [Fact]
    public void Sidebar_badge_follows_the_notification_count()
    {
        var center = new FakeCenter();
        var vm = Create(new FakeNavigation(), center: center);
        var inbox = vm.FooterItems.Single(i => i.Title == "Notifications");

        center.Active = [new("k", Nexstrap.Services.Notifications.NotificationCategory.Configuration, "t", "b", Nexstrap.Services.Notifications.NotificationTarget.Home)];
        center.Refresh();
        Assert.Equal(1, inbox.Badge);
        Assert.True(inbox.HasBadge);

        center.Active = [];
        center.Refresh();
        Assert.False(inbox.HasBadge);
    }

    [Fact]
    public void Selecting_a_nav_item_navigates_and_keeps_single_selection()
    {
        var nav = new FakeNavigation();
        var vm = Create(nav);
        vm.NavigateToStartPage("FastFlags");

        var settingsPage = vm.NavItems.Single(i => i.Title == "Roblox Settings");
        settingsPage.IsSelected = true;   // what a click, arrow key or UI Automation does

        Assert.IsType<RobloxSettingsViewModel>(nav.CurrentPage);
        Assert.Equal([typeof(FastFlagsViewModel), typeof(RobloxSettingsViewModel)], nav.History);
        Assert.Single(vm.NavItems.Concat(vm.FooterItems), i => i.IsSelected);
    }

    [Fact]
    public void Sidebar_follows_saved_changes_and_hidden_pages_still_open()
    {
        var settings = new MemorySettings();
        var nav = new FakeNavigation();
        var vm = Create(nav, settings: settings);
        Assert.Contains(vm.NavItems, i => i.Title == "Servers");

        settings.Current.Sidebar.Hidden.Add("Servers");
        settings.Current.Sidebar.Order = ["Games", "Home"];
        settings.Save();

        Assert.DoesNotContain(vm.NavItems, i => i.Title == "Servers");
        Assert.Equal("Games", vm.NavItems[0].Title);

        vm.NavigateToStartPage("Servers");
        Assert.IsType<ServersViewModel>(nav.CurrentPage);
    }

    [Fact]
    public void Every_sidebar_page_has_a_nav_item()
    {
        var vm = Create(new FakeNavigation());
        Assert.Equal(Nexstrap.Core.SidebarLayout.Pages, vm.NavItems.Select(i => i.Title));
    }

    [Fact]
    public void Unknown_start_page_falls_back_to_home()
    {
        var nav = new FakeNavigation();
        var vm = Create(nav);
        vm.NavigateToStartPage("does-not-exist");
        Assert.Equal([typeof(HomeViewModel)], nav.History);
    }

    [Fact]
    public async Task Launch_update_check_runs_only_when_enabled()
    {
        var updates = new FakeUpdates();
        var settings = new MemorySettings();
        var vm = Create(new FakeNavigation(), updates, settings);

        await vm.CheckForUpdatesOnLaunchAsync();
        Assert.Equal(1, updates.Checks);

        settings.Current.Launcher.CheckForUpdatesOnStartup = false;
        await vm.CheckForUpdatesOnLaunchAsync();
        Assert.Equal(1, updates.Checks);
    }
}
