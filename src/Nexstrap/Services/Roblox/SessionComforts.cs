using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

public interface ISessionComforts
{
    /// <summary>Applies the settings again (after a toggle changes).</summary>
    void Refresh();
}

/// <summary>
/// Small things that make a Roblox session nicer, while Roblox started from Nexstrap is open:
/// keeping the PC from going to sleep.
/// </summary>
public sealed class SessionComforts : ISessionComforts, IDisposable
{
    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001;

    private readonly IRobloxSessionService _session;
    private readonly ISettingsService _settings;
    private readonly ILogger<SessionComforts> _logger;
    private bool _keepingAwake;

    public SessionComforts(IRobloxSessionService session, ISettingsService settings, ILogger<SessionComforts> logger)
    {
        _session = session;
        _settings = settings;
        _logger = logger;
        session.Changed += (_, _) => OnUi(Refresh);
    }

    public void Refresh()
    {
        SetKeepAwake(_settings.Current.Launcher.KeepAwakeWhilePlaying && _session.IsActive);
    }

    /// <summary>
    /// Asks Windows not to sleep while Roblox is open (for AFK grinding or long loads). The screen can still turn off,
    /// and normal sleep comes back as soon as Roblox closes or Nexstrap exits.
    /// The request belongs to the thread that makes it, so it's always made on the window's thread.
    /// </summary>
    private void SetKeepAwake(bool on)
    {
        if (on == _keepingAwake) return;
        _keepingAwake = on;
        var result = SetThreadExecutionState(on ? EsContinuous | EsSystemRequired : EsContinuous);
        if (result == 0) _logger.LogWarning("Keep awake: Windows didn't accept the request");
        else _logger.LogInformation("Keep awake: {State}", on ? "on while Roblox is open" : "off");
    }

    private static void OnUi(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher) dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        if (_keepingAwake) SetThreadExecutionState(EsContinuous);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
