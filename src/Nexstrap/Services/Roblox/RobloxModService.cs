using System.Buffers.Binary;
using System.IO;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

public interface IRobloxModService
{
    /// <summary>Uses an .ogg file as the death sound. Returns an error message, or null on success.</summary>
    string? SetDeathSound(string path);

    /// <summary>Uses a .png file as the mouse cursor. Returns an error message, or null on success.</summary>
    string? SetCursor(string path);

    void ClearDeathSound();

    void ClearCursor();

    /// <summary>Brings a Nexstrap build in line with the settings: applies the files, or restores the originals.</summary>
    void ApplyTo(RobloxInstallation installation);
}

/// <summary>
/// Swaps Roblox's default death sound and mouse cursor. Only Nexstrap-installed builds are touched.
/// Originals are kept in a backup folder inside the build, so removing a mod restores them exactly.
/// </summary>
public sealed class RobloxModService : IRobloxModService
{
    internal const string BackupFolder = ".nexstrap-mods-backup";
    internal const int MaxCursorSize = 256;

    internal static readonly string[] DeathSoundTargets = [@"content\sounds\ouch.ogg"];

    internal static readonly string[] CursorTargets =
    [
        @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",
        @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png"
    ];

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly ISettingsService _settings;
    private readonly ILogger<RobloxModService> _logger;
    private readonly string _modsFolder;

    public RobloxModService(ISettingsService settings, ILogger<RobloxModService> logger)
        : this(settings, logger, Path.Combine(AppInfo.Paths.Root, "Mods")) { }

    internal RobloxModService(ISettingsService settings, ILogger<RobloxModService> logger, string modsFolder)
    {
        _settings = settings;
        _logger = logger;
        _modsFolder = modsFolder;
    }

    private RobloxModSettings Options => _settings.Current.RobloxMods;

    public string? SetDeathSound(string path)
    {
        if (!path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) return Loc.Get("Appearance.SoundNeedsOgg");
        if (!File.Exists(path)) return Loc.Get("Appearance.ThatFileNoLongerExists");

        try
        {
            if (!IsOgg(ReadHeader(path))) return Loc.Get("Appearance.ThatFileIsntOgg");
            Options.DeathSoundFile = CopyIn(path, "death-sound.ogg");
            Options.DeathSoundName = Path.GetFileName(path);
            _settings.Save();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not use sound {Path}", path);
            return Loc.Get("Appearance.ThatFileIsntOgg");
        }
    }

    public string? SetCursor(string path)
    {
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return Loc.Get("Appearance.CursorNeedsPng");
        if (!File.Exists(path)) return Loc.Get("Appearance.ThatFileNoLongerExists");

        try
        {
            if (!IsUsableCursor(ReadHeader(path))) return Loc.Get("Appearance.CursorNeedsPng");
            Options.CursorFile = CopyIn(path, "cursor.png");
            Options.CursorName = Path.GetFileName(path);
            _settings.Save();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not use cursor {Path}", path);
            return Loc.Get("Appearance.CursorNeedsPng");
        }
    }

    public void ClearDeathSound()
    {
        Options.DeathSoundFile = Options.DeathSoundName = null;
        _settings.Save();
    }

    public void ClearCursor()
    {
        Options.CursorFile = Options.CursorName = null;
        _settings.Save();
    }

    public void ApplyTo(RobloxInstallation installation)
    {
        if (installation.Source != InstallationSource.Nexstrap) return;
        Sync(installation.Directory);
    }

    internal void Sync(string buildDirectory)
    {
        foreach (var target in DeathSoundTargets) SyncFile(buildDirectory, target, Options.DeathSoundFile);
        foreach (var target in CursorTargets) SyncFile(buildDirectory, target, Options.CursorFile);

        var backups = Path.Combine(buildDirectory, BackupFolder);
        if (Directory.Exists(backups) && !Directory.EnumerateFiles(backups, "*", SearchOption.AllDirectories).Any())
            Directory.Delete(backups, recursive: true);
    }

    private void SyncFile(string buildDirectory, string relativePath, string? custom)
    {
        var target = Path.Combine(buildDirectory, relativePath);
        var backup = Path.Combine(buildDirectory, BackupFolder, relativePath);

        if (custom is not null && File.Exists(custom))
        {
            if (!File.Exists(target))
            {
                _logger.LogWarning("Roblox has no {File} to replace", relativePath);
                return;
            }
            // The first time only: later runs must not back up an already-replaced file.
            if (!File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup);
            }
            File.Copy(custom, target, overwrite: true);
        }
        else if (File.Exists(backup))
        {
            File.Copy(backup, target, overwrite: true);
            File.Delete(backup);
        }
    }

    private string CopyIn(string path, string name)
    {
        Directory.CreateDirectory(_modsFolder);
        var target = Path.Combine(_modsFolder, name);
        File.Copy(path, target, overwrite: true);
        return target;
    }

    private static byte[] ReadHeader(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[24];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return header[..read];
    }

    internal static bool IsOgg(ReadOnlySpan<byte> header) => header.StartsWith("OggS"u8);

    /// <summary>Checks the PNG signature and the size stored in its first chunk.</summary>
    internal static bool IsUsableCursor(ReadOnlySpan<byte> header)
    {
        if (header.Length < 24 || !header.StartsWith(PngSignature)) return false;
        var width = BinaryPrimitives.ReadInt32BigEndian(header[16..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[20..]);
        return width is > 0 and <= MaxCursorSize && height is > 0 and <= MaxCursorSize;
    }
}
