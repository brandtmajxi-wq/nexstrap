using System.Globalization;
using System.IO;
using System.Xml;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;

namespace Nexstrap.Services.Roblox;

/// <summary>
/// The values Roblox's own in-game Settings menu stores. Null means the file doesn't have the key
/// (Roblox then uses its default).
/// </summary>
public sealed record RobloxUserSettings
{
    /// <summary>In-game "Maximum Frame Rate".</summary>
    public int? FramerateCap { get; init; }

    /// <summary>0 = Automatic, 1–10 = the in-game graphics slider.</summary>
    public int? SavedQualityLevel { get; init; }

    public bool? Fullscreen { get; init; }

    /// <summary>0–1.</summary>
    public double? MasterVolume { get; init; }

    public double? MouseSensitivity { get; init; }

    public bool? CameraYInverted { get; init; }
}

public sealed record SettingsBackup(string FilePath, DateTime CreatedLocal);

public interface IRobloxUserSettingsService
{
    /// <summary>False until Roblox has been run at least once on this PC.</summary>
    bool Exists { get; }

    string FilePath { get; }

    RobloxUserSettings Load();

    /// <summary>Writes only the non-null values. Refuses while Roblox runs; backs up first.</summary>
    void Save(RobloxUserSettings changes);

    SettingsBackup BackupNow();

    IReadOnlyList<SettingsBackup> ListBackups();

    void Restore(SettingsBackup backup);
}

/// <summary>
/// Edits %LocalAppData%\Roblox\GlobalBasicSettings_13.xml in place: only the touched elements
/// change, everything else (unknown keys, formatting) is preserved.
/// </summary>
public sealed class RobloxUserSettingsService : IRobloxUserSettingsService
{
    private const int MaxBackups = 15;

    private readonly IRobloxLauncher _launcher;
    private readonly ILogger<RobloxUserSettingsService> _logger;
    private readonly string _backupDir;

    public RobloxUserSettingsService(IRobloxLauncher launcher, ILogger<RobloxUserSettingsService> logger)
        : this(launcher, logger,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "GlobalBasicSettings_13.xml"),
            AppInfo.Paths.Backups) { }

    internal RobloxUserSettingsService(IRobloxLauncher launcher, ILogger<RobloxUserSettingsService> logger, string filePath, string backupDir)
    {
        _launcher = launcher;
        _logger = logger;
        FilePath = filePath;
        _backupDir = backupDir;
    }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public RobloxUserSettings Load()
    {
        if (!Exists) return new RobloxUserSettings();
        var props = LoadDocument().Properties;
        return new RobloxUserSettings
        {
            FramerateCap = ReadInt(props, "int", "FramerateCap"),
            SavedQualityLevel = ReadInt(props, "token", "SavedQualityLevel"),
            Fullscreen = ReadBool(props, "Fullscreen"),
            MasterVolume = ReadDouble(props, "MasterVolume"),
            MouseSensitivity = ReadDouble(props, "MouseSensitivity"),
            CameraYInverted = ReadBool(props, "CameraYInverted")
        };
    }

    public void Save(RobloxUserSettings changes)
    {
        if (!Exists)
            throw new InvalidOperationException(Loc.Get("RobloxSettings.RobloxHasntCreatedItsSettingsFile"));
        if (_launcher.IsRobloxRunning())
            throw new InvalidOperationException(Loc.Get("RobloxSettings.CloseRobloxFirstItSavesIts"));

        var (doc, props) = LoadDocument();
        BackupNow();

        if (changes.FramerateCap is { } fps) Write(doc, props, "int", "FramerateCap", fps.ToString(CultureInfo.InvariantCulture));
        if (changes.SavedQualityLevel is { } q) Write(doc, props, "token", "SavedQualityLevel", Math.Clamp(q, 0, 10).ToString(CultureInfo.InvariantCulture));
        if (changes.Fullscreen is { } fs) Write(doc, props, "bool", "Fullscreen", fs ? "true" : "false");
        if (changes.MasterVolume is { } v) Write(doc, props, "float", "MasterVolume", Math.Clamp(v, 0, 1).ToString("0.###", CultureInfo.InvariantCulture));
        if (changes.MouseSensitivity is { } s) Write(doc, props, "float", "MouseSensitivity", Math.Clamp(s, 0.1, 4).ToString("0.###", CultureInfo.InvariantCulture));
        if (changes.CameraYInverted is { } inv) Write(doc, props, "bool", "CameraYInverted", inv ? "true" : "false");

        var temp = FilePath + ".nexstrap-tmp";
        using (var writer = XmlWriter.Create(temp, new XmlWriterSettings { Indent = false, Encoding = new System.Text.UTF8Encoding(false) }))
            doc.Save(writer);
        File.Move(temp, FilePath, overwrite: true);
        _logger.LogInformation("Updated Roblox settings: {Changes}", changes);
    }

    public SettingsBackup BackupNow()
    {
        if (!Exists) throw new InvalidOperationException(Loc.Get("RobloxSettings.TheresNoRobloxSettingsFileTo"));
        Directory.CreateDirectory(_backupDir);
        var path = Path.Combine(_backupDir, $"GlobalBasicSettings-{DateTime.Now:yyyyMMdd-HHmmss-fff}.xml");
        File.Copy(FilePath, path, overwrite: true);

        foreach (var old in Directory.GetFiles(_backupDir, "GlobalBasicSettings-*.xml").OrderByDescending(f => f).Skip(MaxBackups))
            File.Delete(old);
        return new SettingsBackup(path, DateTime.Now);
    }

    public IReadOnlyList<SettingsBackup> ListBackups() =>
        Directory.Exists(_backupDir)
            ? Directory.GetFiles(_backupDir, "GlobalBasicSettings-*.xml")
                .OrderByDescending(f => f)
                .Select(f => new SettingsBackup(f, File.GetCreationTime(f)))
                .ToList()
            : [];

    public void Restore(SettingsBackup backup)
    {
        if (_launcher.IsRobloxRunning())
            throw new InvalidOperationException(Loc.Get("RobloxSettings.CloseRobloxBeforeRestoringItsSettings"));

        // Make sure the backup is a Roblox settings document before it replaces the real one.
        var check = new XmlDocument();
        check.Load(backup.FilePath);
        if (check.SelectSingleNode("/roblox/Item/Properties") is null)
            throw new InvalidDataException(Loc.Get("RobloxSettings.ThatBackupIsntARobloxSettings"));

        if (Exists) BackupNow();
        File.Copy(backup.FilePath, FilePath, overwrite: true);
        _logger.LogInformation("Restored Roblox settings from {Backup}", backup.FilePath);
    }

    // ===== XML helpers =====

    private (XmlDocument Doc, XmlElement Properties) LoadDocument()
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(FilePath);
        var props = doc.SelectSingleNode("/roblox/Item/Properties") as XmlElement
                    ?? throw new InvalidDataException("Roblox's settings file has an unexpected layout.");
        return (doc, props);
    }

    private static XmlElement? Find(XmlElement props, string name) =>
        props.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.GetAttribute("name") == name);

    private static int? ReadInt(XmlElement props, string type, string name) =>
        Find(props, name) is { } e && e.LocalName == type && int.TryParse(e.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool? ReadBool(XmlElement props, string name) =>
        Find(props, name) is { LocalName: "bool" } e && bool.TryParse(e.InnerText, out var v) ? v : null;

    private static double? ReadDouble(XmlElement props, string name) =>
        Find(props, name) is { LocalName: "float" } e && double.TryParse(e.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static void Write(XmlDocument doc, XmlElement props, string type, string name, string value)
    {
        var element = Find(props, name);
        if (element is null)
        {
            element = doc.CreateElement(type);
            element.SetAttribute("name", name);
            props.AppendChild(element);
        }
        else if (element.LocalName != type)
        {
            throw new InvalidDataException($"Roblox stores {name} in an unexpected format; leaving it unchanged.");
        }
        element.InnerText = value;
    }
}
