using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

public interface IRobloxFontService
{
    /// <summary>Uses a .ttf/.otf file. Returns an error message, or null on success.</summary>
    string? SetFontFile(string path);

    /// <summary>Uses an installed font by name. Returns an error message, or null on success.</summary>
    string? SetInstalledFont(string familyName);

    /// <summary>Brings a Nexstrap build in line with the setting: applies the font, or restores the originals.</summary>
    void ApplyTo(RobloxInstallation installation);
}

/// <summary>
/// Points Roblox's font families (content\fonts\families\*.json) at one custom font.
/// Only Nexstrap-installed builds are touched. The original family files are kept in a backup folder
/// inside the build, so turning the feature off restores them exactly.
/// </summary>
public sealed class RobloxFontService : IRobloxFontService
{
    internal const string BackupFolder = ".nexstrap-font-backup";
    internal const string CustomFontName = "NexstrapCustomFont";

    /// <summary>Left alone so text in scripts the custom font lacks (Chinese, Japanese, Korean) still shows.</summary>
    private static readonly string[] SkippedFamilies = ["NotoSansCJKFallback"];

    private static readonly string[] SupportedExtensions = [".ttf", ".otf"];

    private readonly ISettingsService _settings;
    private readonly ILogger<RobloxFontService> _logger;
    private readonly string _fontsFolder;

    public RobloxFontService(ISettingsService settings, ILogger<RobloxFontService> logger)
        : this(settings, logger, Path.Combine(AppInfo.Paths.Root, "Fonts")) { }

    internal RobloxFontService(ISettingsService settings, ILogger<RobloxFontService> logger, string fontsFolder)
    {
        _settings = settings;
        _logger = logger;
        _fontsFolder = fontsFolder;
    }

    private RobloxFontSettings Options => _settings.Current.RobloxFont;

    public string? SetFontFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext)) return Loc.Get("Appearance.RobloxNeedsATtfOrOtf");
        if (!File.Exists(path)) return Loc.Get("Appearance.ThatFileNoLongerExists");

        try
        {
            if (!new GlyphTypeface(new Uri(path)).CharacterToGlyphMap.ContainsKey('A'))
                return Loc.Get("Appearance.ThatFontHasNoNormalLetters");

            Directory.CreateDirectory(_fontsFolder);
            var target = Path.Combine(_fontsFolder, CustomFontName + ext);
            foreach (var old in SupportedExtensions) if (old != ext) File.Delete(Path.Combine(_fontsFolder, CustomFontName + old));
            File.Copy(path, target, overwrite: true);

            Options.FontFile = target;
            Options.DisplayName = Path.GetFileNameWithoutExtension(path);
            _settings.Save();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileFormatException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Could not use font {Path}", path);
            return Loc.Get("Appearance.ThatFileCouldntBeReadAs2");
        }
    }

    public string? SetInstalledFont(string familyName)
    {
        var language = XmlLanguage.GetLanguage("en-us");
        var family = Fonts.SystemFontFamilies.FirstOrDefault(f =>
            string.Equals(f.Source, familyName, StringComparison.OrdinalIgnoreCase)
            || (f.FamilyNames.TryGetValue(language, out var name) && string.Equals(name, familyName, StringComparison.OrdinalIgnoreCase)));
        if (family is null) return Loc.Format("Appearance.IsntInstalledAsASeparateFont", familyName);

        var regular = family.GetTypefaces()
            .OrderBy(t => t.Weight == System.Windows.FontWeights.Normal ? 0 : 1)
            .ThenBy(t => t.Style == System.Windows.FontStyles.Normal ? 0 : 1)
            .FirstOrDefault();
        if (regular is null || !regular.TryGetGlyphTypeface(out var glyphs) || !glyphs.FontUri.IsFile)
            return Loc.Format("Appearance.WindowsDidntReportAFileFor", familyName);

        var error = SetFontFile(glyphs.FontUri.LocalPath);
        if (error is not null) return error;
        Options.DisplayName = familyName;
        _settings.Save();
        return null;
    }

    public void ApplyTo(RobloxInstallation installation)
    {
        if (installation.Source != InstallationSource.Nexstrap) return;

        if (Options.Enabled && Options.FontFile is { } font && File.Exists(font))
            Apply(installation.Directory, font);
        else
            Restore(installation.Directory);
    }

    internal void Apply(string buildDirectory, string fontFile)
    {
        var fonts = Path.Combine(buildDirectory, "content", "fonts");
        var families = Path.Combine(fonts, "families");
        var backup = Path.Combine(buildDirectory, BackupFolder);
        if (!Directory.Exists(families)) return;

        // The first time only: later runs must not back up already-changed files.
        if (!Directory.Exists(backup))
        {
            var staging = backup + ".tmp";
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            foreach (var file in Directory.GetFiles(families, "*.json"))
                File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));
            Directory.Move(staging, backup);
        }

        DeleteCustomFonts(fonts);
        var fileName = CustomFontName + Path.GetExtension(fontFile).ToLowerInvariant();
        File.Copy(fontFile, Path.Combine(fonts, fileName), overwrite: true);

        var changed = 0;
        foreach (var original in Directory.GetFiles(backup, "*.json"))
        {
            if (SkippedFamilies.Contains(Path.GetFileNameWithoutExtension(original), StringComparer.OrdinalIgnoreCase)) continue;
            var rewritten = RewriteFamily(File.ReadAllText(original), "rbxasset://fonts/" + fileName);
            if (rewritten is null) continue;
            File.WriteAllText(Path.Combine(families, Path.GetFileName(original)), rewritten);
            changed++;
        }
        _logger.LogInformation("Applied custom Roblox font to {Count} families", changed);
    }

    internal void Restore(string buildDirectory)
    {
        var backup = Path.Combine(buildDirectory, BackupFolder);
        if (!Directory.Exists(backup)) return;

        var fonts = Path.Combine(buildDirectory, "content", "fonts");
        foreach (var file in Directory.GetFiles(backup, "*.json"))
            File.Copy(file, Path.Combine(fonts, "families", Path.GetFileName(file)), overwrite: true);
        DeleteCustomFonts(fonts);
        Directory.Delete(backup, recursive: true);
        _logger.LogInformation("Restored Roblox's original fonts in {Dir}", buildDirectory);
    }

    /// <summary>Returns the family JSON with every face pointing at the font, or null if it isn't a font family file.</summary>
    internal static string? RewriteFamily(string json, string assetId)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return null; }

        if (root?["faces"] is not JsonArray faces || faces.Count == 0) return null;
        foreach (var face in faces.OfType<JsonObject>()) face["assetId"] = assetId;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void DeleteCustomFonts(string fonts)
    {
        foreach (var file in Directory.GetFiles(fonts, CustomFontName + ".*")) File.Delete(file);
    }
}
