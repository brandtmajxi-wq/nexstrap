using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nexstrap.Core;
using Nexstrap.Models;

namespace Nexstrap.Services.Roblox;

public interface IRobloxLocator
{
    /// <summary>All Roblox Player builds found on disk, newest first.</summary>
    IReadOnlyList<RobloxInstallation> FindInstallations();

    /// <summary>Command registered for the roblox-player: protocol (HKCU, then HKCR).</summary>
    ProtocolHandlerInfo GetProtocolHandler();
}

/// <summary>
/// Read-only discovery of Roblox Player builds. Only well-known version folders are scanned;
/// nothing is modified.
/// </summary>
public sealed class RobloxLocator(ILogger<RobloxLocator> logger) : IRobloxLocator
{
    public const string PlayerExecutable = "RobloxPlayerBeta.exe";

    /// <summary>Root folders that contain version-xxxxxxxx sub-folders, per source.</summary>
    internal static IEnumerable<(InstallationSource Source, string Root)> SearchRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        yield return (InstallationSource.Nexstrap, AppInfo.Paths.Versions);
        yield return (InstallationSource.Official, Path.Combine(local, "Roblox", "Versions"));
        if (!string.IsNullOrEmpty(x86))
            yield return (InstallationSource.Official, Path.Combine(x86, "Roblox", "Versions"));
        yield return (InstallationSource.Bloxstrap, Path.Combine(local, "Bloxstrap", "Versions"));
        yield return (InstallationSource.Voidstrap, Path.Combine(local, "Voidstrap", "RblxVersions"));
        yield return (InstallationSource.Voidstrap, Path.Combine(local, "Voidstrap", "Versions"));
        yield return (InstallationSource.Fishstrap, Path.Combine(local, "Fishstrap", "Versions"));
    }

    public IReadOnlyList<RobloxInstallation> FindInstallations() => Scan(SearchRoots());

    internal IReadOnlyList<RobloxInstallation> Scan(IEnumerable<(InstallationSource Source, string Root)> roots)
    {
        var found = new List<RobloxInstallation>();

        foreach (var (source, root) in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "version-*"))
                {
                    var exe = Path.Combine(dir, PlayerExecutable);
                    if (!File.Exists(exe)) continue;
                    // Our own builds only count once fully installed and verified.
                    if (source == InstallationSource.Nexstrap && !File.Exists(Path.Combine(dir, RobloxDeployment.CompleteMarker))) continue;

                    found.Add(new RobloxInstallation(
                        source,
                        dir,
                        exe,
                        Path.GetFileName(dir),
                        ReadClientVersion(exe),
                        File.GetLastWriteTime(exe)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not scan {Root}", root);
            }
        }

        logger.LogInformation("Found {Count} Roblox Player installation(s)", found.Count);
        return found.OrderByDescending(i => i.LastModified).ToList();
    }

    public ProtocolHandlerInfo GetProtocolHandler()
    {
        const string key = @"roblox-player\shell\open\command";
        try
        {
            using var user = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + key);
            var command = user?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(command))
            {
                using var machine = Registry.ClassesRoot.OpenSubKey(key);
                command = machine?.GetValue(null) as string;
            }
            return new ProtocolHandlerInfo(command);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the roblox-player protocol handler");
            return new ProtocolHandlerInfo(null);
        }
    }

    internal static string? ReadClientVersion(string exe)
    {
        try
        {
            return NormaliseFileVersion(FileVersionInfo.GetVersionInfo(exe).FileVersion);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// "0, 745, 0, 7451137" → "0.745.0.7451137". The string is used rather than the numeric
    /// parts because Roblox's last component exceeds the 16-bit FilePrivatePart.
    /// </summary>
    internal static string? NormaliseFileVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split([',', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !parts.All(p => p.All(char.IsDigit)) || parts.All(p => p.Trim('0').Length == 0))
            return null;
        return string.Join('.', parts);
    }
}
