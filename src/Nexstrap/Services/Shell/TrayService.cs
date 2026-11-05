using System.Windows;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;
using Forms = System.Windows.Forms;

namespace Nexstrap.Services.Shell;

public interface ITrayService : IDisposable
{
    void Show();

    /// <summary>A Windows pop-up from the tray icon. Does nothing while there's no icon.</summary>
    void ShowAlert(string title, string text);
}

/// <summary>The notification-area icon: open Nexstrap, start Roblox or a recent game, or quit.</summary>
public sealed class TrayService(ISettingsService settings, ILaunchCoordinator launch, ILastServerService lastServer) : ITrayService
{
    internal const int MaxRecentGames = 5;

    private Forms.NotifyIcon? _icon;

    public void Show()
    {
        if (_icon is not null) return;

        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/Branding/Nexstrap.ico")).Stream;
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => Fill(menu);
        // WinForms needs at least one item, or the first right-click shows nothing.
        Fill(menu);

        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = AppInfo.Name,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Open(); };
    }

    public void ShowAlert(string title, string text) =>
        Application.Current?.Dispatcher.BeginInvoke(() => _icon?.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None));

    /// <summary>Rebuilt each time, so it follows the language and the latest games.</summary>
    internal void Fill(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add(Loc.Get("Tray.Open"), null, (_, _) => Open());
        menu.Items.Add(Loc.Get("Home.PlayRoblox"), null, (_, _) => _ = launch.LaunchAppAsync());

        if (lastServer.Available is { } server)
        {
            var name = server.Name.Length > 0 ? server.Name : Loc.Format("Launch.Place", server.PlaceId);
            menu.Items.Add(Loc.Format("Tray.Rejoin", name).Replace("&", "&&"), null, (_, _) => _ = lastServer.RejoinAsync());
        }

        var recent = settings.Current.Launcher.RecentExperiences.Take(MaxRecentGames).ToList();
        if (recent.Count > 0)
        {
            var games = new Forms.ToolStripMenuItem(Loc.Get("Tray.RecentGames"));
            foreach (var game in recent)
            {
                // "&" would otherwise underline the next letter of the game's name.
                games.DropDownItems.Add(game.Name.Replace("&", "&&"), null, (_, _) => _ = launch.JoinAsync(game.PlaceId, game.Name));
            }
            menu.Items.Add(games);
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.Get("Tray.Quit"), null, (_, _) => Application.Current.Shutdown());
    }

    private static void Open() => (Application.Current as App)?.ShowMainWindow();

    public void Dispose()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _icon = null;
    }
}
