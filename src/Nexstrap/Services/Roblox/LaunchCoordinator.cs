using System.Windows;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Discord;
using Nexstrap.Services.Integrations;

namespace Nexstrap.Services.Roblox;

public interface ILaunchCoordinator
{
    /// <summary>Opens the Roblox app. Null means the user cancelled or nothing could be launched.</summary>
    Task<LaunchResult?> LaunchAppAsync();

    /// <summary>Joins an experience, or one specific server when <paramref name="serverId"/> is given.</summary>
    Task<LaunchResult?> JoinAsync(long placeId, string? name, string? serverId = null);

    /// <summary>Raised when the recent-experiences list changed.</summary>
    event EventHandler? RecentsChanged;
}

/// <summary>
/// The one path every page uses to start Roblox: confirmations, launching, session tracking,
/// remembering recent games and "close after launch".
/// </summary>
public sealed class LaunchCoordinator(
    IRobloxClientService client,
    IRobloxLauncher launcher,
    IRobloxSessionService session,
    IRichPresenceService presence,
    ICompanionAppService companions,
    ISettingsService settings,
    IDialogService dialogs,
    ILaunchScreen screen,
    IPlayHistoryService history) : ILaunchCoordinator
{
    internal const int MaxRecent = 20;

    public event EventHandler? RecentsChanged;

    public Task<LaunchResult?> LaunchAppAsync() => RunAsync(i => launcher.LaunchApp(i), replacesGame: false, onStarted: null);

    public Task<LaunchResult?> JoinAsync(long placeId, string? name, string? serverId = null)
    {
        var uri = serverId is null ? RobloxLinks.PlaceLaunchUri(placeId) : RobloxLinks.ServerLaunchUri(placeId, serverId);
        return RunAsync(i => launcher.LaunchUri(i, uri), replacesGame: true, () => RememberRecent(placeId, name));
    }

    private async Task<LaunchResult?> RunAsync(Func<RobloxInstallation, LaunchResult> launch, bool replacesGame, Action? onStarted)
    {
        var state = client.State;
        if (state.Preferred is not { } installation)
        {
            dialogs.Show(Loc.Get("App.RobloxIsntInstalled"), Loc.Get("Launch.InstallRobloxFromTheHomePage"), DialogKind.Warning);
            return null;
        }

        if (replacesGame && launcher.IsRobloxRunning() &&
            !dialogs.Confirm(Loc.Get("Launch.RobloxIsAlreadyOpen"),
                Loc.Get("Launch.RobloxRunsOneGameAtA"), Loc.Get("Launch.JoinAnyway")))
            return null;

        if (state.UpdateAvailable && state.Latest is { } latest &&
            !dialogs.Confirm(Loc.Get("Launch.RobloxIsOutOfDate"),
                Loc.Format("Launch.ThisBuildIsButRobloxIs", installation.DisplayVersion, latest.Version),
                Loc.Get("Launch.LaunchAnyway"), Loc.Get("Launch.NotNow")))
            return null;

        var launchedAt = DateTime.UtcNow;
        var result = await Task.Run(() => launch(installation));
        if (!result.Success)
        {
            dialogs.Show(Loc.Get("App.RobloxDidntStart"), result.Message, DialogKind.Error);
            return result;
        }

        if (result.ProcessId is { } pid)
        {
            session.Track(pid, launchedAt);
            screen.ShowUntilReady(pid);
        }
        onStarted?.Invoke();

        if (settings.Current.Launcher.CloseAfterLaunch)
        {
            await Task.Delay(1500); // let Roblox take focus first
            if (presence.KeepsRunningDuringSession || companions.NeedsToWatchSession || history.IsRecording)
            {
                // Presence and companion cleanup live as long as Nexstrap does: hide instead of exiting.
                Application.Current.MainWindow?.Hide();
                await session.WaitForEndAsync();
            }
            Application.Current.Shutdown();
        }
        return result;
    }

    internal void RememberRecent(long placeId, string? name)
    {
        var list = settings.Current.Launcher.RecentExperiences;
        var previous = list.FirstOrDefault(r => r.PlaceId == placeId);
        list.RemoveAll(r => r.PlaceId == placeId);
        list.Insert(0, new RecentExperience { PlaceId = placeId, Name = name ?? previous?.Name ?? Loc.Format("Launch.Place", placeId), LastLaunched = DateTime.Now });
        if (list.Count > MaxRecent) list.RemoveRange(MaxRecent, list.Count - MaxRecent);
        settings.Save();
        RecentsChanged?.Invoke(this, EventArgs.Empty);
    }
}
