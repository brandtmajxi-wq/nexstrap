using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.FastFlags;

namespace Nexstrap.Services.Roblox;

public sealed record LaunchResult(bool Success, string Message, int? ProcessId = null);

public interface IRobloxLauncher
{
    /// <summary>Opens the Roblox Player to its home screen (the same thing the official shortcut does).</summary>
    LaunchResult LaunchApp(RobloxInstallation installation);

    /// <summary>Hands a roblox: / roblox-player: link to the client, exactly as Windows would.</summary>
    LaunchResult LaunchUri(RobloxInstallation installation, string uri);

    /// <summary>True while any Roblox Player process is running.</summary>
    bool IsRobloxRunning();
}

/// <summary>
/// Starts the Roblox Player the supported way: run the official client executable from its
/// own folder, passing either "--app" or a Roblox deep link. Sign-in, server selection and
/// anti-cheat are handled entirely by Roblox.
/// </summary>
public sealed class RobloxLauncher(
    IFastFlagService fastFlags,
    IRobloxFontService fonts,
    IRobloxModService mods,
    IRobloxProcessTuner tuner,
    ILogger<RobloxLauncher> logger) : IRobloxLauncher
{
    private const string ProcessName = "RobloxPlayerBeta";

    public LaunchResult LaunchApp(RobloxInstallation installation) =>
        Start(installation, "--app", "the Roblox app");

    public LaunchResult LaunchUri(RobloxInstallation installation, string uri)
    {
        if (!RobloxLinks.IsLaunchUri(uri))
            return Fail(Loc.Get("Launch.ThatIsntARobloxLaunchLink"));
        return Start(installation, uri, RobloxLinks.Redact(uri));
    }

    public bool IsRobloxRunning()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private LaunchResult Start(RobloxInstallation installation, string argument, string description)
    {
        if (!File.Exists(installation.ExecutablePath))
            return Fail(Loc.Format("Launch.RobloxIsNoLongerAtRefresh", installation.Directory));

        // Roblox reads ClientAppSettings.json at startup, so flags must be in place first.
        string? flagWarning = null;
        try
        {
            fastFlags.ApplyTo(installation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write FastFlags");
            flagWarning = Loc.Get("Launch.YourFastFlagsCouldntBeWritten") + ex.Message + ")";
        }

        try
        {
            fonts.ApplyTo(installation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Files can be in use if Roblox is already open; the next launch tries again.
            logger.LogWarning(ex, "Could not update Roblox's fonts");
            flagWarning += Loc.Get("Launch.YourRobloxFontCouldntBeUpdated") + ex.Message + ")";
        }

        try
        {
            mods.ApplyTo(installation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not update Roblox's sound or cursor");
            flagWarning += Loc.Get("Launch.YourRobloxModsCouldntBeUpdated") + ex.Message + ")";
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = installation.ExecutablePath,
                WorkingDirectory = installation.Directory,
                UseShellExecute = false
            };
            // ArgumentList quotes for us; the URI was validated to contain no spaces or quotes.
            info.ArgumentList.Add(argument);

            var process = Process.Start(info);
            if (process is null)
                return Fail(Loc.Get("Launch.WindowsDidNotStartRoblox"));

            tuner.Tune(process);
            logger.LogInformation("Started Roblox {Version} with {What} (pid {Pid})",
                installation.DisplayVersion, description, process.Id);
            return new LaunchResult(true, Loc.Get("Launch.RobloxIsStarting") + flagWarning, process.Id);
        }
        catch (Win32Exception ex)
        {
            logger.LogError(ex, "Failed to start {Exe}", installation.ExecutablePath);
            return Fail(Loc.Format("Launch.WindowsCouldNotStartRoblox", ex.Message));
        }
    }

    private LaunchResult Fail(string message)
    {
        logger.LogWarning("Launch failed: {Message}", message);
        return new LaunchResult(false, message);
    }
}
