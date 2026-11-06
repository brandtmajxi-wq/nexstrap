using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Nexstrap.Core;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Services.Shell;

public enum HotkeyResult { AllWorking, SomeWorking, NoneWorking }

public interface IHotkeyService : IDisposable
{
    /// <summary>Turns the keys on or off. Each key works by itself, so one clash doesn't stop the other.</summary>
    HotkeyResult Apply(bool enabled);
}

/// <summary>Ctrl+Alt+N opens Nexstrap and Ctrl+Alt+J rejoins the last server, from any app.</summary>
public sealed class HotkeyService(ILastServerService lastServer, ITrayService tray) : IHotkeyService
{
    private const int HotkeyMessage = 0x0312;
    private const int OpenId = 1;
    private const int RejoinId = 2;
    private const uint Modifiers = 0x0001 | 0x0002 | 0x4000; // Alt, Ctrl, no auto-repeat
    private const uint KeyN = 0x4E;
    private const uint KeyJ = 0x4A;

    private HwndSource? _window;

    public HotkeyResult Apply(bool enabled)
    {
        Unregister();
        if (!enabled) return HotkeyResult.AllWorking;

        // Its own hidden window, so the keys also work while Nexstrap waits in the tray.
        if (_window is null)
        {
            _window = new HwndSource(new HwndSourceParameters("NexstrapHotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
            _window.AddHook(OnMessage);
        }

        var open = RegisterHotKey(_window.Handle, OpenId, Modifiers, KeyN);
        var rejoin = RegisterHotKey(_window.Handle, RejoinId, Modifiers, KeyJ);
        return open && rejoin ? HotkeyResult.AllWorking : open || rejoin ? HotkeyResult.SomeWorking : HotkeyResult.NoneWorking;
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != HotkeyMessage) return IntPtr.Zero;

        handled = true;
        if (wParam.ToInt32() == OpenId)
            (Application.Current as App)?.ShowMainWindow();
        else if (lastServer.Available is not null)
            _ = lastServer.RejoinAsync();
        else
            tray.ShowAlert(AppInfo.DisplayName, Loc.Get("RobloxSettings.Hotkeys.NothingToRejoin"));
        return IntPtr.Zero;
    }

    private void Unregister()
    {
        if (_window is null) return;
        UnregisterHotKey(_window.Handle, OpenId);
        UnregisterHotKey(_window.Handle, RejoinId);
    }

    public void Dispose()
    {
        Unregister();
        _window?.Dispose();
        _window = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
