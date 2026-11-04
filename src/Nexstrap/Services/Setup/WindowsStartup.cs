using Microsoft.Win32;
using Nexstrap.Core;

namespace Nexstrap.Services.Setup;

public interface IWindowsStartup
{
    /// <summary>True when Windows opens this copy of Nexstrap at sign-in.</summary>
    bool IsEnabled { get; }

    void Enable();

    void Disable();
}

/// <summary>The per-user Run entry, so no admin rights are needed.</summary>
public sealed class WindowsStartup : IWindowsStartup
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _key;
    private readonly string _exe;

    public WindowsStartup() : this(RunKey, Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.")) { }

    internal WindowsStartup(string key, string exe)
    {
        _key = key;
        _exe = exe;
    }

    public bool IsEnabled => Read(_key) == Command(_exe);

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_key);
        key.SetValue(AppInfo.Name, Command(_exe));
    }

    public void Disable() => Remove(_key);

    internal static string Command(string exe) => $"\"{exe}\"";

    internal static string? Read(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(AppInfo.Name) as string;
    }

    internal static void Remove(string keyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }

    /// <summary>Keeps an existing entry working when Nexstrap moves to a new folder.</summary>
    internal static void Retarget(string keyPath, string exe)
    {
        if (Read(keyPath) is null) return;
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.SetValue(AppInfo.Name, Command(exe));
    }
}
