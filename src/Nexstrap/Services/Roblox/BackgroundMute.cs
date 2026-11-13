using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

public interface IBackgroundMute
{
    /// <summary>Starts or stops after the setting changes. Turning it off unmutes Roblox straight away.</summary>
    void Refresh();
}

/// <summary>
/// Mutes Roblox while another window is in front and unmutes it when you switch back, using Roblox's own entry in
/// the Windows volume mixer (the same switch as muting it there by hand). Nothing inside Roblox is touched.
/// Only Nexstrap's own mute is undone: if you muted Roblox in the mixer yourself, it stays muted.
/// </summary>
public sealed class BackgroundMute : IBackgroundMute, IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(300);

    private readonly ISettingsService _settings;
    private readonly ILogger<BackgroundMute> _logger;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly HashSet<int> _mutedByUs = [];
    private bool? _lastInFront;
    private int _ticks;
    private bool _running;

    public BackgroundMute(ISettingsService settings, ILogger<BackgroundMute> logger)
    {
        _settings = settings;
        _logger = logger;
        _timer = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
        Refresh();
    }

    public void Refresh()
    {
        var on = _settings.Current.Launcher.MuteRobloxInBackground;
        lock (_gate)
        {
            if (on == _running) return;
            _running = on;
            _lastInFront = null;
        }
        if (on) _timer.Change(TimeSpan.Zero, Tick);
        else
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            ThreadPool.QueueUserWorkItem(_ => UnmuteAll());
        }
    }

    private void Check()
    {
        if (!Monitor.TryEnter(_gate)) return; // the last check is still running
        try
        {
            if (!_running) return;
            var roblox = RobloxProcessIds();
            if (roblox.Count == 0)
            {
                _mutedByUs.Clear();
                _lastInFront = null;
                return;
            }

            var inFront = roblox.Contains(ForegroundProcessId());
            // Act when focus changes, and every 3 seconds in the background, since Roblox can open new sound outputs
            // (for example after joining a game).
            if (inFront == _lastInFront && (inFront || ++_ticks % 10 != 0)) return;
            _lastInFront = inFront;
            Apply(roblox, mute: !inFront);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Background mute: couldn't reach Windows' volume mixer");
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void Apply(HashSet<int> roblox, bool mute)
    {
        foreach (var (pid, volume) in Sessions())
        {
            try
            {
                if (!roblox.Contains(pid)) continue;
                var context = Guid.Empty;
                volume.GetMute(out var muted);
                if (mute && muted == 0)
                {
                    volume.SetMute(1, ref context);
                    _mutedByUs.Add(pid);
                }
                else if (!mute && muted != 0 && _mutedByUs.Contains(pid))
                {
                    volume.SetMute(0, ref context);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }
        if (!mute) _mutedByUs.Clear();
    }

    private void UnmuteAll()
    {
        lock (_gate)
        {
            if (_mutedByUs.Count == 0) return;
            try { Apply([.. _mutedByUs], mute: false); }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                _logger.LogDebug(ex, "Background mute: couldn't unmute Roblox");
            }
            _mutedByUs.Clear();
        }
    }

    private static HashSet<int> RobloxProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta"))
            using (process) ids.Add(process.Id);
        return ids;
    }

    private static int ForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out var pid);
        return (int)pid;
    }

    /// <summary>Every sound output on every active speaker or headset, with the process it belongs to.</summary>
    internal static IEnumerable<(int ProcessId, ISimpleAudioVolume Volume)> Sessions()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.EnumAudioEndpoints(0 /* render */, 1 /* active */, out var devices) != 0) yield break;
            devices.GetCount(out var deviceCount);
            for (var d = 0; d < deviceCount; d++)
            {
                if (devices.Item(d, out var device) != 0) continue;
                var iid = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var managerObject) != 0) continue;
                var manager = (IAudioSessionManager2)managerObject;
                if (manager.GetSessionEnumerator(out var sessions) != 0) continue;
                sessions.GetCount(out var count);
                for (var i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var control) != 0) continue;
                    if (control is IAudioSessionControl2 control2 && control2.GetProcessId(out var pid) == 0 && control is ISimpleAudioVolume volume)
                        yield return ((int)pid, volume);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        lock (_gate) _running = false;
        UnmuteAll();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(IntPtr group, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint processId);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute(int mute, ref Guid context);
        [PreserveSig] int GetMute(out int mute);
    }
}
