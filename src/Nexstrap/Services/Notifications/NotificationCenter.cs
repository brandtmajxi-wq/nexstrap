using Nexstrap.Core;
using System.IO;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.FastFlags;
using Nexstrap.Services.Performance;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Updates;

namespace Nexstrap.Services.Notifications;

public enum NotificationCategory { NexstrapUpdate, RobloxUpdate, Configuration }

/// <summary>The page that can fix what a notification describes.</summary>
public enum NotificationTarget { Home, FastFlags, Performance }

public sealed record AppNotification(string Key, NotificationCategory Category, string Title, string Body, NotificationTarget Target)
{
    public bool IsWarning => Category == NotificationCategory.Configuration;

    public string CategoryLabel => Category switch
    {
        NotificationCategory.NexstrapUpdate => Loc.Get("Notifications.NexstrapUpdate"),
        NotificationCategory.RobloxUpdate => Loc.Get("Notifications.RobloxUpdate"),
        _ => Loc.Get("Notifications.Configuration")
    };
}

/// <summary>Everything the rules look at, gathered so they stay pure and testable.</summary>
public sealed record NotificationInputs(
    Version? NexstrapUpdate,
    RobloxInstallation? Installation,
    ClientVersionInfo? LatestRoblox,
    IReadOnlyList<FastFlagEntry> Flags,
    int? FramerateCap,
    int? RefreshRate);

public static class NotificationRules
{
    public static IReadOnlyList<AppNotification> Evaluate(NotificationInputs input)
    {
        var list = new List<AppNotification>();

        if (input.NexstrapUpdate is { } version)
            list.Add(new($"nexstrap-update:{version}", NotificationCategory.NexstrapUpdate, Loc.Format("Notifications.NexstrapIsAvailable", version),
                Loc.Get("Notifications.OpenHomeToReadWhatsNew"), NotificationTarget.Home));

        if (input.LatestRoblox is { } latest && input.Installation is { } build && build.VersionHash != latest.VersionHash)
            list.Add(new($"roblox-update:{latest.VersionHash}", NotificationCategory.RobloxUpdate, Loc.Format("Notifications.RobloxIsAvailable", latest.Version),
                Loc.Format("Notifications.PlayIsUsingOutdatedClientsMay", build.DisplayVersion, build.SourceLabel), NotificationTarget.Home));

        var enabled = input.Flags.Where(f => f.Enabled).ToList();

        var ignored = enabled.Count(f => !FastFlagCatalog.IsAllowlisted(f.Name));
        if (ignored > 0)
            list.Add(new($"config-ignored-flags:{ignored}", NotificationCategory.Configuration,
                ignored == 1 ? Loc.Get("Notifications.1FastFlagIsIgnoredByRoblox") : Loc.Format("Notifications.FastFlagsAreIgnoredByRoblox", ignored),
                Loc.Get("Notifications.TheyArentOnRobloxsAllowlistSo"), NotificationTarget.FastFlags));

        if (enabled.Count > 0 && input.Installation is { Source: not InstallationSource.Nexstrap } other)
            list.Add(new($"config-flags-wrong-build:{other.Source}", NotificationCategory.Configuration, Loc.Get("Notifications.YourFastFlagsArentBeingApplied"),
                Loc.Format("Notifications.PlayUsesSBuildAndNexstrap", other.SourceLabel),
                NotificationTarget.Home));

        if (input.FramerateCap is { } cap && !Nexstrap.Services.Performance.PerformancePlanner.IsNoLimit(cap) && input.RefreshRate is { } hz && cap > hz + 1)
            list.Add(new($"config-cap-above-display:{cap}-{hz}", NotificationCategory.Configuration, Loc.Format("Notifications.FrameRateCapIsAboveYour", hz),
                Loc.Format("Notifications.YourCapIsFPSButFrames", cap, hz), NotificationTarget.Performance));

        return list;
    }

    public static bool IsEnabled(NotificationCategory category, NotificationSettings options) => category switch
    {
        NotificationCategory.NexstrapUpdate => options.NexstrapUpdates,
        NotificationCategory.RobloxUpdate => options.RobloxUpdates,
        _ => options.ConfigurationWarnings
    };
}

public interface INotificationCenter
{
    /// <summary>Notifications to show: not dismissed, and in a category that's turned on.</summary>
    IReadOnlyList<AppNotification> Active { get; }

    /// <summary>Raised when the list may have changed. May be raised on a background thread.</summary>
    event EventHandler? Changed;

    void Refresh();

    void Dismiss(AppNotification notification);

    void DismissAll();

    /// <summary>Shows everything that was dismissed again.</summary>
    void RestoreDismissed();
}

/// <summary>Re-evaluates the rules whenever updates, Roblox state or FastFlags change.</summary>
public sealed class NotificationCenter : INotificationCenter
{
    private const int MaxDismissed = 100;

    private readonly ISettingsService _settings;
    private readonly IUpdateService _updates;
    private readonly IRobloxClientService _client;
    private readonly IFastFlagService _flags;
    private readonly IRobloxUserSettingsService _robloxSettings;
    private readonly IDisplayInfo _display;
    private IReadOnlyList<AppNotification> _all = [];

    public NotificationCenter(ISettingsService settings, IUpdateService updates, IRobloxClientService client, IFastFlagService flags,
        IRobloxUserSettingsService robloxSettings, IDisplayInfo display)
    {
        _settings = settings;
        _updates = updates;
        _client = client;
        _flags = flags;
        _robloxSettings = robloxSettings;
        _display = display;

        updates.Checked += (_, _) => Refresh();
        client.StateChanged += (_, _) => Refresh();
        flags.Changed += (_, _) => Refresh();
        Loc.Changed += (_, _) => Refresh();
    }

    private NotificationSettings Options => _settings.Current.Notifications;

    public IReadOnlyList<AppNotification> Active =>
        _all.Where(n => NotificationRules.IsEnabled(n.Category, Options) && !Options.Dismissed.Contains(n.Key)).ToList();

    public event EventHandler? Changed;

    public void Refresh()
    {
        int? cap;
        try { cap = _robloxSettings.Load().FramerateCap; }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or InvalidDataException) { cap = null; }

        var update = _updates.LastResult is { Status: UpdateStatus.UpdateAvailable } result ? result.LatestVersion : null;
        var state = _client.State;
        _all = NotificationRules.Evaluate(new NotificationInputs(update, state.Preferred, state.Latest, _flags.Flags.ToList(), cap, _display.RefreshRate));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dismiss(AppNotification notification) => AddDismissed([notification.Key]);

    public void DismissAll() => AddDismissed(Active.Select(n => n.Key));

    public void RestoreDismissed()
    {
        Options.Dismissed.Clear();
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddDismissed(IEnumerable<string> keys)
    {
        foreach (var key in keys.Where(k => !Options.Dismissed.Contains(k))) Options.Dismissed.Add(key);
        if (Options.Dismissed.Count > MaxDismissed) Options.Dismissed.RemoveRange(0, Options.Dismissed.Count - MaxDismissed);
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
