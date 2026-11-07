using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Shell;

namespace Nexstrap.Services.Notifications;

public interface ICrashAlerts;

/// <summary>When Roblox stops suddenly, offers to go back to the same server or open the logs.</summary>
public sealed class CrashAlerts : ICrashAlerts
{
    private readonly ISettingsService _settings;
    private readonly ILastServerService _lastServer;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly ILogger<CrashAlerts> _logger;
    private readonly Func<bool> _searching;
    private readonly Func<Func<Task>, Task> _onUiThread;

    public CrashAlerts(IRobloxSessionService session, ISettingsService settings, ILastServerService lastServer, IDialogService dialogs,
        IShellService shell, IServerRegionService regions, ILogger<CrashAlerts> logger)
        : this(session, settings, lastServer, dialogs, shell, logger, () => regions.IsSearching, OnUiThreadAsync) { }

    internal CrashAlerts(IRobloxSessionService session, ISettingsService settings, ILastServerService lastServer, IDialogService dialogs,
        IShellService shell, ILogger<CrashAlerts> logger, Func<bool> searching, Func<Func<Task>, Task> onUiThread)
    {
        _settings = settings;
        _lastServer = lastServer;
        _dialogs = dialogs;
        _shell = shell;
        _logger = logger;
        _searching = searching;
        _onUiThread = onUiThread;
        session.Crashed += (_, crash) => Handle(crash);
    }

    /// <summary>Blocks until it's answered, so a Nexstrap that's waiting for the session to end stays open for the question.</summary>
    internal void Handle(RobloxCrash crash)
    {
        // A region search closes Roblox itself.
        if (!_settings.Current.Notifications.CrashAlerts || _searching()) return;

        var title = Loc.Get("Crash.Title");
        if (ServerToRejoin(crash) is not { } server)
        {
            if (_dialogs.Choose(title, Loc.Get("Crash.Text"), Loc.Get("Crash.OpenLogs"), null, Loc.Get("Crash.Close")) == DialogChoice.Confirm)
                OpenLogs(crash);
            return;
        }

        var name = server.Name.Length > 0 ? server.Name : Loc.Format("Launch.Place", server.PlaceId);
        switch (_dialogs.Choose(title, Loc.Format("Crash.InGame", name), Loc.Get("Crash.Rejoin"), Loc.Get("Crash.OpenLogs"), Loc.Get("Crash.Close")))
        {
            case DialogChoice.Confirm: Rejoin(); break;
            case DialogChoice.Extra: OpenLogs(crash); break;
        }
    }

    private LastServerInfo? ServerToRejoin(RobloxCrash crash) =>
        crash.PlaceId is { } place && _lastServer.Available is { } server && server.PlaceId == place ? server : null;

    private void Rejoin()
    {
        try { _onUiThread(_lastServer.RejoinAsync).GetAwaiter().GetResult(); }
        catch (Exception ex) { _logger.LogError(ex, "Rejoining after a crash failed"); }
    }

    private void OpenLogs(RobloxCrash crash)
    {
        if (Path.GetDirectoryName(crash.LogPath) is { } folder) _shell.OpenFolder(folder);
    }

    private static Task OnUiThreadAsync(Func<Task> work) =>
        Application.Current?.Dispatcher.InvokeAsync(work).Task.Unwrap() ?? Task.CompletedTask;
}
