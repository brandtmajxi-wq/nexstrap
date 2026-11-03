using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.FastFlags;

public sealed record FlagImportResult(int Added, int Updated, IReadOnlyList<string> Skipped);

public sealed record FlagBackup(string FilePath, DateTime CreatedLocal, string Reason, int FlagCount);

public interface IFastFlagService
{
    IReadOnlyList<FastFlagEntry> Flags { get; }

    event EventHandler? Changed;

    /// <summary>Adds or replaces a flag. Returns an error message instead of throwing on bad input.</summary>
    string? Set(string name, string value, bool enabled = true);

    void SetEnabled(string name, bool enabled);

    void Remove(string name);

    /// <summary>Parses a {"Name": value} JSON object (Bloxstrap/Voidstrap/Roblox format).</summary>
    FlagImportResult Import(string json, bool replaceExisting);

    /// <summary>The enabled flags as a Roblox-style JSON object.</summary>
    string Export();

    void Reset();

    /// <summary>Replaces the whole list (keeping disabled entries), after a backup. Invalid entries are dropped.</summary>
    int ReplaceAll(IEnumerable<FastFlagEntry> flags);

    IReadOnlyList<FlagBackup> ListBackups();

    void Restore(FlagBackup backup);

    /// <summary>
    /// Writes enabled flags into a Nexstrap-managed build's ClientSettings\ClientAppSettings.json.
    /// Builds owned by other launchers are never written to.
    /// </summary>
    void ApplyTo(RobloxInstallation installation);

    /// <summary>Reads (never writes) the flags file of any installation, e.g. to import from Voidstrap.</summary>
    string? ReadFromInstallation(RobloxInstallation installation);
}

/// <summary>
/// Owns the user's flag list (FastFlags.json). Every bulk change (import, reset, restore) and
/// the first edit of each session is preceded by a timestamped backup.
/// </summary>
public sealed class FastFlagService : IFastFlagService
{
    private const int MaxBackups = 20;
    private const string SettingsFolder = "ClientSettings";
    private const string SettingsFile = "ClientAppSettings.json";

    private readonly JsonFileStore<FastFlagDocument> _store;
    private readonly ILogger<FastFlagService> _logger;
    private readonly string _backupDir;
    private readonly FastFlagDocument _doc;
    private bool _backedUpThisSession;

    public FastFlagService(ILogger<FastFlagService> logger)
        : this(logger, Path.Combine(AppInfo.Paths.Root, "FastFlags.json"), AppInfo.Paths.Backups) { }

    internal FastFlagService(ILogger<FastFlagService> logger, string path, string backupDir)
    {
        _logger = logger;
        _backupDir = backupDir;
        _store = new JsonFileStore<FastFlagDocument>(path, logger);
        _doc = _store.Load();
        // Drop anything a hand-edited file may contain that isn't a valid flag.
        _doc.Flags.RemoveAll(f => FastFlagCatalog.ValidateName(f.Name) is not null);
    }

    public IReadOnlyList<FastFlagEntry> Flags => _doc.Flags;

    public event EventHandler? Changed;

    public string? Set(string name, string value, bool enabled = true)
    {
        name = name.Trim();
        if (FastFlagCatalog.ValidateName(name) is { } nameError) return nameError;
        if (FastFlagCatalog.ValidateValue(name, value) is { } valueError) return valueError;

        BackupOncePerSession();
        var normalised = FastFlagCatalog.Normalise(name, value);
        var existing = Find(name);
        if (existing is null)
            _doc.Flags.Add(new FastFlagEntry { Name = name, Value = normalised, Enabled = enabled });
        else
        {
            existing.Value = normalised;
            existing.Enabled = enabled;
        }
        Commit();
        return null;
    }

    public void SetEnabled(string name, bool enabled)
    {
        if (Find(name) is not { } entry || entry.Enabled == enabled) return;
        BackupOncePerSession();
        entry.Enabled = enabled;
        Commit();
    }

    public void Remove(string name)
    {
        BackupOncePerSession();
        if (_doc.Flags.RemoveAll(f => f.Name.Equals(name.Trim(), StringComparison.Ordinal)) > 0)
            Commit();
    }

    public FlagImportResult Import(string json, bool replaceExisting)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                   ?? throw new InvalidDataException("The file must contain a JSON object like { \"FlagName\": value }.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("That isn't valid JSON: " + ex.Message, ex);
        }

        var accepted = new List<(string Name, string Value)>();
        var skipped = new List<string>();
        foreach (var (name, node) in root)
        {
            var text = node switch
            {
                JsonValue v when v.TryGetValue<bool>(out var b) => b ? "True" : "False",
                JsonValue v when v.TryGetValue<long>(out var l) => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                _ => null
            };
            var error = text is null ? "unsupported value type" : FastFlagCatalog.ValidateName(name) ?? FastFlagCatalog.ValidateValue(name, text);
            if (error is null) accepted.Add((name.Trim(), FastFlagCatalog.Normalise(name, text!)));
            else skipped.Add($"{name}: {error}");
        }

        CreateBackup(replaceExisting ? "Before import (replace)" : "Before import (merge)");
        if (replaceExisting) _doc.Flags.Clear();

        int added = 0, updated = 0;
        foreach (var (name, value) in accepted)
        {
            if (Find(name) is { } existing)
            {
                existing.Value = value;
                existing.Enabled = true;
                updated++;
            }
            else
            {
                _doc.Flags.Add(new FastFlagEntry { Name = name, Value = value });
                added++;
            }
        }

        Commit();
        _logger.LogInformation("Imported flags: {Added} added, {Updated} updated, {Skipped} skipped", added, updated, skipped.Count);
        return new FlagImportResult(added, updated, skipped);
    }

    public string Export() => BuildRobloxJson(_doc.Flags);

    public void Reset()
    {
        CreateBackup("Before reset");
        _doc.Flags.Clear();
        Commit();
    }

    public int ReplaceAll(IEnumerable<FastFlagEntry> flags)
    {
        var valid = flags
            .Where(f => FastFlagCatalog.ValidateName(f.Name) is null && FastFlagCatalog.ValidateValue(f.Name, f.Value) is null)
            .DistinctBy(f => f.Name.Trim(), StringComparer.Ordinal)
            .Select(f => new FastFlagEntry { Name = f.Name.Trim(), Value = FastFlagCatalog.Normalise(f.Name, f.Value), Enabled = f.Enabled })
            .ToList();

        CreateBackup("Before importing a Nexstrap configuration");
        _doc.Flags.Clear();
        _doc.Flags.AddRange(valid);
        Commit();
        return valid.Count;
    }

    public IReadOnlyList<FlagBackup> ListBackups()
    {
        if (!Directory.Exists(_backupDir)) return [];
        var list = new List<FlagBackup>();
        foreach (var file in Directory.EnumerateFiles(_backupDir, "FastFlags-*.json"))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(file), JsonFileStore<FastFlagDocument>.SerializerOptions);
                if (doc is not null) list.Add(new FlagBackup(file, File.GetCreationTime(file), doc.Reason, doc.Flags.Count));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                _logger.LogDebug(ex, "Skipping unreadable backup {File}", file);
            }
        }
        return list.OrderByDescending(b => b.CreatedLocal).ToList();
    }

    public void Restore(FlagBackup backup)
    {
        var doc = JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(backup.FilePath), JsonFileStore<FastFlagDocument>.SerializerOptions)
                  ?? throw new InvalidDataException("That backup is empty.");
        CreateBackup("Before restoring a backup");
        _doc.Flags.Clear();
        _doc.Flags.AddRange(doc.Flags.Where(f => FastFlagCatalog.ValidateName(f.Name) is null
                                                 && FastFlagCatalog.ValidateValue(f.Name, f.Value) is null));
        Commit();
    }

    public void ApplyTo(RobloxInstallation installation)
    {
        if (installation.Source != InstallationSource.Nexstrap)
        {
            _logger.LogInformation("Not writing FastFlags into {Source}'s build", installation.Source);
            return;
        }

        var folder = Path.Combine(installation.Directory, SettingsFolder);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, SettingsFile);
        var temp = target + ".tmp";
        File.WriteAllText(temp, BuildRobloxJson(_doc.Flags));
        File.Move(temp, target, overwrite: true);
        _logger.LogInformation("Wrote {Count} FastFlags to {Path}", _doc.Flags.Count(f => f.Enabled), target);
    }

    public string? ReadFromInstallation(RobloxInstallation installation)
    {
        var path = Path.Combine(installation.Directory, SettingsFolder, SettingsFile);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>Roblox format: enabled flags only, values as strings (what Roblox and other launchers use).</summary>
    internal static string BuildRobloxJson(IEnumerable<FastFlagEntry> flags)
    {
        var root = new JsonObject();
        foreach (var f in flags.Where(f => f.Enabled)) root[f.Name] = f.Value;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed class BackupFile
    {
        public string Reason { get; set; } = "";
        public List<FastFlagEntry> Flags { get; set; } = [];
    }

    private void BackupOncePerSession()
    {
        if (_backedUpThisSession) return;
        _backedUpThisSession = true;
        if (_doc.Flags.Count > 0) CreateBackup("Before first edit this session");
    }

    private void CreateBackup(string reason)
    {
        try
        {
            Directory.CreateDirectory(_backupDir);
            var path = Path.Combine(_backupDir, $"FastFlags-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new BackupFile { Reason = reason, Flags = _doc.Flags.ToList() },
                JsonFileStore<FastFlagDocument>.SerializerOptions));

            foreach (var old in Directory.GetFiles(_backupDir, "FastFlags-*.json").OrderByDescending(f => f).Skip(MaxBackups))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "FastFlags backup failed");
        }
    }

    private FastFlagEntry? Find(string name) =>
        _doc.Flags.FirstOrDefault(f => f.Name.Equals(name.Trim(), StringComparison.Ordinal));

    private void Commit()
    {
        _store.Save(_doc);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
