using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

public interface IRobloxProcessTuner
{
    /// <summary>Applies the CPU priority, CPU core limit and window title to a Roblox process Nexstrap just started.</summary>
    void Tune(Process process);
}

/// <summary>
/// Uses ordinary Windows process settings on the process Nexstrap started. Nothing inside Roblox is read or changed.
/// A failure only means Roblox runs with Windows' defaults, so it's logged and otherwise ignored.
/// </summary>
public sealed class RobloxProcessTuner(ISettingsService settings, ILogger<RobloxProcessTuner> logger) : IRobloxProcessTuner
{
    internal const int MaxTitleLength = 100;

    /// <summary>Roblox can take a while to open its window, and may set its own title just after.</summary>
    private const int TitleWaitSeconds = 120;
    private const int TitleRepeats = 5;

    public void Tune(Process process)
    {
        var options = settings.Current.Launcher;
        try
        {
            if (ToPriorityClass(options.RobloxPriority) is { } priority) process.PriorityClass = priority;
            if (AffinityMask(options.RobloxCoreLimit, Environment.ProcessorCount) is { } mask) process.ProcessorAffinity = mask;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            logger.LogWarning(ex, "Could not set Roblox's CPU priority or cores");
        }

        if (CleanTitle(options.RobloxWindowTitle) is { } title)
            _ = ApplyTitleAsync(process, title, TitleWaitSeconds, TimeSpan.FromSeconds(1));
    }

    internal static ProcessPriorityClass? ToPriorityClass(RobloxPriority priority) => priority switch
    {
        RobloxPriority.BelowNormal => ProcessPriorityClass.BelowNormal,
        RobloxPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
        RobloxPriority.High => ProcessPriorityClass.High,
        _ => null
    };

    /// <summary>The first <paramref name="limit"/> logical processors, or null when the limit doesn't restrict anything.</summary>
    internal static nint? AffinityMask(int limit, int processors)
    {
        if (limit <= 0 || limit >= processors || limit >= 63) return null;
        return (nint)((1L << limit) - 1);
    }

    internal static string? CleanTitle(string? title)
    {
        var clean = new string((title ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return null;
        return clean.Length > MaxTitleLength ? clean[..MaxTitleLength] : clean;
    }

    internal async Task ApplyTitleAsync(Process process, string title, int attempts, TimeSpan delay)
    {
        var applied = 0;
        try
        {
            for (var i = 0; i < attempts && applied < TitleRepeats && !process.HasExited; i++)
            {
                process.Refresh();
                if (process.MainWindowHandle != 0 && SetWindowTextW(process.MainWindowHandle, title)) applied++;
                await Task.Delay(delay);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            logger.LogDebug(ex, "Roblox closed while setting its window title");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint window, string text);
}
