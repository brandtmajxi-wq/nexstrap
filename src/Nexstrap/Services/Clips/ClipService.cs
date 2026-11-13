using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;
using Nexstrap.Views;

namespace Nexstrap.Services.Clips;

public interface IClipService
{
    /// <summary>Dev only for now, and needs Windows' screen capture (Windows 10 version 1903 or newer).</summary>
    bool Available { get; }

    bool IsRecording { get; }

    /// <summary>Where saved clips go: Videos\Nexstrap Clips.</summary>
    string ClipsFolder { get; }

    /// <summary>Saves the last 1:30 and shows a note in the corner. Does nothing if nothing is being recorded.</summary>
    Task SaveAsync();

    /// <summary>Starts or stops recording, and sets the save key again, after a setting changes.</summary>
    void Refresh();

    /// <summary>False when another app already uses the chosen key.</summary>
    bool HotkeyWorks { get; }

    /// <summary>Lets go of the save key while a new one is being picked, so pressing the old one is seen by the picker.</summary>
    bool HotkeyPaused { get; set; }

    event EventHandler? Changed;
}

/// <summary>
/// Clips: while Roblox is open (and the setting is on), the last minute and a half of the Roblox window and
/// the PC's sound are kept, and the save key (Ctrl+Alt+C unless changed) saves the last 30 s, 1 min or 1:30 as an MP4 in Videos\Nexstrap Clips. Only Roblox's window
/// is recorded, nothing else on the screen, and nothing is kept once Roblox closes.
/// </summary>
public sealed class ClipService : IClipService, IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const int ClipHotkeyId = 31;
    private const uint NoRepeat = 0x4000;

    private readonly IRobloxSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ILogger<ClipService> _logger;
    private readonly DispatcherTimer _findWindow = new() { Interval = TimeSpan.FromSeconds(2) };
    private ClipRecorder? _recorder;
    private HwndSource? _hotkeyWindow;
    private bool _saving, _paused;
    private (uint Modifiers, uint Key)? _registered;

    public ClipService(IRobloxSessionService session, ISettingsService settings, ILogger<ClipService> logger)
    {
        _session = session;
        _settings = settings;
        _logger = logger;
        _findWindow.Tick += (_, _) => TryStart();
        session.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    public event EventHandler? Changed;

    public bool Available => AppInfo.IsDev && ClipRecorder.IsSupported;

    public bool IsRecording => _recorder is not null;

    public string ClipsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Nexstrap Clips");

    private bool Wanted => Available && _settings.Current.Launcher.RecordClips;

    public bool HotkeyWorks { get; private set; } = true;

    public bool HotkeyPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            SetHotkey(Wanted && !value);
        }
    }

    public void Refresh()
    {
        SetHotkey(Wanted && !_paused);
        if (Wanted && _session.IsActive)
        {
            if (_recorder is null) TryStart();
        }
        else
        {
            Stop();
        }
    }

    private void TryStart()
    {
        if (_recorder is not null || !Wanted || !_session.IsActive)
        {
            _findWindow.Stop();
            return;
        }

        // Roblox's window can take a few seconds to appear after the process starts.
        var window = FindRobloxWindow();
        if (window == IntPtr.Zero)
        {
            _findWindow.Start();
            return;
        }
        _findWindow.Stop();

        try
        {
            _recorder = ClipRecorder.Start(window, Path.Combine(AppInfo.Paths.Root, "ClipBuffer"), _logger);
            _recorder.Closed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => { Stop(); if (_session.IsActive) _findWindow.Start(); });
            _logger.LogInformation("Clips: recording the Roblox window");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Clips: couldn't start recording");
            _recorder = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        _findWindow.Stop();
        if (_recorder is null || _saving) return;
        _recorder.Dispose();
        _recorder = null;
        _logger.LogInformation("Clips: stopped recording");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IntPtr FindRobloxWindow()
    {
        foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
                catch (InvalidOperationException) { }
            }
        }
        return IntPtr.Zero;
    }

    public async Task SaveAsync()
    {
        if (_recorder is not { } recorder)
        {
            ReminderToast.Pop(Loc.Get("Clips.NotRecording"), Loc.Get("Clips.NotRecording.Text"));
            return;
        }
        if (_saving) return;

        _saving = true;
        ReminderToast.Pop(Loc.Get("Clips.Saving"), Loc.Get("Clips.Saving.Text"));
        try
        {
            var length = TimeSpan.FromSeconds(Math.Clamp(_settings.Current.Launcher.ClipSeconds, 10, (int)ClipRecorder.ClipLength.TotalSeconds));
            var path = await Task.Run(() => recorder.SaveAsync(ClipsFolder, $"Roblox {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4", length));
            ReminderToast.Pop(Loc.Get("Clips.Saved"), Path.GetFileName(path));
            _logger.LogInformation("Clips: saved {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Clips: saving failed");
            ReminderToast.Pop(Loc.Get("Clips.Failed"), ex.Message);
        }
        finally
        {
            _saving = false;
            if (!_session.IsActive) Stop();
        }
    }

    // ===== The save key, from any app (including Roblox in front) =====

    private void SetHotkey(bool on)
    {
        var launcher = _settings.Current.Launcher;
        (uint, uint)? wanted = on ? (launcher.ClipHotkeyModifiers, launcher.ClipHotkeyKey) : null;
        if (wanted == _registered) return;

        if (_registered is not null && _hotkeyWindow is not null)
        {
            UnregisterHotKey(_hotkeyWindow.Handle, ClipHotkeyId);
            _registered = null;
        }
        HotkeyWorks = true;
        if (wanted is { } key)
        {
            _hotkeyWindow ??= CreateHotkeyWindow();
            if (RegisterHotKey(_hotkeyWindow.Handle, ClipHotkeyId, key.Item1 | NoRepeat, key.Item2))
            {
                _registered = key;
            }
            else
            {
                HotkeyWorks = false;
                _logger.LogWarning("Clips: {Key} is already used by another app", ClipHotkey.Format(key.Item1, key.Item2));
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private HwndSource CreateHotkeyWindow()
    {
        var window = new HwndSource(new HwndSourceParameters("NexstrapClips") { Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == HotkeyMessage && wParam.ToInt32() == ClipHotkeyId)
            {
                handled = true;
                _ = SaveAsync();
            }
            return IntPtr.Zero;
        });
        return window;
    }

    public void Dispose()
    {
        SetHotkey(false);
        _hotkeyWindow?.Dispose();
        _recorder?.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
