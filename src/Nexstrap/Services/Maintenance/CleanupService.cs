using System.IO;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Services.Maintenance;

public sealed record CleanupResult(int Files, long Bytes);

public interface ICleanupService
{
    /// <summary>Deletes old log, temp and download files. Does nothing while Roblox is open.</summary>
    CleanupResult CleanOldFiles();
}

/// <summary>
/// Removes files older than a week from a fixed list of folders that only hold logs and temporary files.
/// Folders themselves, linked folders and files in use are left alone.
/// </summary>
public sealed class CleanupService : ICleanupService
{
    internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly IRobloxLauncher _launcher;
    private readonly ILogger<CleanupService> _logger;
    private readonly IReadOnlyList<string> _folders;

    public CleanupService(IRobloxLauncher launcher, ILogger<CleanupService> logger)
        : this(launcher, logger,
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs"),
            Path.Combine(Path.GetTempPath(), "Roblox"),
            AppInfo.Paths.Downloads
        ]) { }

    internal CleanupService(IRobloxLauncher launcher, ILogger<CleanupService> logger, IReadOnlyList<string> folders)
    {
        _launcher = launcher;
        _logger = logger;
        _folders = folders;
    }

    public CleanupResult CleanOldFiles()
    {
        if (_launcher.IsRobloxRunning()) return new CleanupResult(0, 0);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };
        var cutoff = DateTime.UtcNow - MaxAge;
        var files = 0;
        long bytes = 0;

        foreach (var folder in _folders.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", options))
            {
                try
                {
                    var file = new FileInfo(path);
                    if (file.LastWriteTimeUtc >= cutoff) continue;
                    var size = file.Length;
                    file.Delete();
                    files++;
                    bytes += size;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Skipped {Path}", path);
                }
            }
        }

        _logger.LogInformation("Cleaned up {Files} old files ({Bytes} bytes)", files, bytes);
        return new CleanupResult(files, bytes);
    }
}
