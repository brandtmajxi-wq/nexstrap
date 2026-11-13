using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Notifications;

namespace Nexstrap.ViewModels.Pages;

public sealed record CrashRow(string Game, string Detail, string LogPath);

public sealed partial class NotificationsViewModel : PageViewModel
{
    private readonly INotificationCenter _center;
    private readonly ISettingsService _settings;
    private readonly INavigationService _navigation;
    private readonly IPlayReminder _reminder;
    private readonly ISessionSummary _summary;
    private readonly IDailyLimit _limit;
    private readonly ICrashHistory _crashes;
    private readonly Nexstrap.Services.Shell.IShellService _shell;
    private readonly Nexstrap.Services.Dialogs.IDialogService _dialogs;

    public NotificationsViewModel(INotificationCenter center, ISettingsService settings, INavigationService navigation, IPlayReminder reminder,
        ISessionSummary summary, IDailyLimit limit, ICrashHistory crashes, Nexstrap.Services.Shell.IShellService shell,
        Nexstrap.Services.Dialogs.IDialogService dialogs)
    {
        _summary = summary;
        _limit = limit;
        _crashes = crashes;
        _shell = shell;
        _dialogs = dialogs;
        crashes.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(RebuildCrashes);
        _center = center;
        _settings = settings;
        _navigation = navigation;
        _reminder = reminder;
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

    public bool CrashAlerts
    {
        get => _settings.Current.Notifications.CrashAlerts;
        set => Change(() => _settings.Current.Notifications.CrashAlerts = value);
    }

    public IReadOnlyList<Choice<int>> PlayReminderChoices { get; } =
        PlayReminder.Choices.Select(m => new Choice<int>(m, () => PlayReminder.Label(m))).ToList();

    public int PlayReminderMinutes
    {
        get => _settings.Current.Notifications.PlayReminderMinutes;
        set => Change(() => _settings.Current.Notifications.PlayReminderMinutes = value);
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
    private void PreviewReminder() => _reminder.Preview();

    public bool SessionSummary
    {
        get => _settings.Current.Notifications.SessionSummary;
        set => Change(() => _settings.Current.Notifications.SessionSummary = value);
    }

    [RelayCommand]
    private void PreviewSummary() => _summary.Preview();

    public IReadOnlyList<Choice<int>> DailyLimitChoices { get; } =
        DailyLimit.Choices.Select(m => new Choice<int>(m, () => PlayReminder.Label(m))).ToList();

    public int DailyLimitMinutes
    {
        get => _settings.Current.Notifications.DailyLimitMinutes;
        set => Change(() => _settings.Current.Notifications.DailyLimitMinutes = value);
    }

    [RelayCommand]
    private void PreviewLimit() => _limit.Preview();

    [RelayCommand]
    private void Dismiss(AppNotification item) => _center.Dismiss(item);

    [RelayCommand]
    private void DismissAll() => _center.DismissAll();

    [RelayCommand]
    private void RestoreDismissed() => _center.RestoreDismissed();

    // ===== Recent crashes =====

    public ObservableCollection<CrashRow> Crashes { get; } = [];

    public bool HasCrashes => Crashes.Count > 0;

    [RelayCommand]
    private void ShowCrashLog(CrashRow row) => _shell.ShowInFolder(row.LogPath);

    [RelayCommand]
    private void ClearCrashes()
    {
        if (_dialogs.Confirm(Loc.Get("Crashes.Clear.Title"), Loc.Get("Crashes.Clear.Text"), Loc.Get("Crashes.Clear")))
            _crashes.Clear();
    }

    private void RebuildCrashes()
    {
        Crashes.Clear();
        foreach (var crash in _crashes.Recent)
        {
            var game = crash.Name.Length > 0 ? crash.Name
                : crash.PlaceId is { } place ? Loc.Format("Launch.Place", place)
                : Loc.Get("Crashes.Menu");
            var when = DateTime.SpecifyKind(crash.TimeUtc, DateTimeKind.Utc).ToLocalTime().ToString("g");
            var detail = crash.ExitCode is { } code ? $"{when} · {Loc.Format("Crashes.ExitCode", code)}" : when;
            Crashes.Add(new CrashRow(game, detail, crash.LogPath));
        }
        OnPropertyChanged(nameof(HasCrashes));
    }

    public override void OnNavigatedTo()
    {
        _center.Refresh();
        RebuildCrashes();
    }

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
