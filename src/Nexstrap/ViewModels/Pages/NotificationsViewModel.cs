using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Notifications;

namespace Nexstrap.ViewModels.Pages;

public sealed partial class NotificationsViewModel : PageViewModel
{
    private readonly INotificationCenter _center;
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;

    public NotificationsViewModel(INotificationCenter center, ISettingsService settings, INavigationService navigation)
    {
        _center = center;
        _settings = settings;
        _navigation = navigation;
        center.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Rebuild);
    }

    public override string Title => Loc.Get("Nav.Notifications");

    public override string Subtitle => Loc.Get("Page.Notifications");

    public ObservableCollection<AppNotification> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public bool HasDismissed => _settings.Current.Notifications.Dismissed.Count > 0;

    public bool NexstrapUpdates
    {
        get => _settings.Current.Notifications.NexstrapUpdates;
        set => Change(() => _settings.Current.Notifications.NexstrapUpdates = value);
    }

    public bool RobloxUpdates
    {
        get => _settings.Current.Notifications.RobloxUpdates;
        set => Change(() => _settings.Current.Notifications.RobloxUpdates = value);
    }

    public bool GameJoinAlerts
    {
        get => _settings.Current.Notifications.GameJoinAlerts;
        set => Change(() => _settings.Current.Notifications.GameJoinAlerts = value);
    }

    public bool ConfigurationWarnings
    {
        get => _settings.Current.Notifications.ConfigurationWarnings;
        set => Change(() => _settings.Current.Notifications.ConfigurationWarnings = value);
    }

    [RelayCommand]
    private void Open(AppNotification item)
    {
        if (item.Category == NotificationCategory.NexstrapUpdate)
        {
            _navigation.ShowUpdates();
            return;
        }

        switch (item.Target)
        {
            case NotificationTarget.FastFlags: _navigation.NavigateTo<FastFlagsViewModel>(); break;
            case NotificationTarget.Performance: _navigation.NavigateTo<PerformanceViewModel>(); break;
            default: _navigation.NavigateTo<HomeViewModel>(); break;
        }
    }

    [RelayCommand]
    private void Dismiss(AppNotification item) => _center.Dismiss(item);

    [RelayCommand]
    private void DismissAll() => _center.DismissAll();

    [RelayCommand]
    private void RestoreDismissed() => _center.RestoreDismissed();

    public override void OnNavigatedTo() => _center.Refresh();

    private void Change(Action change)
    {
        change();
        _settings.Save();
        OnPropertyChanged(string.Empty);
        _center.Refresh();
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var item in _center.Active) Items.Add(item);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasDismissed));
    }
}
