using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Appearance;

public interface IAppearanceService
{
    AppearanceSettings Current { get; }

    /// <summary>Raised after any appearance value changed and was applied.</summary>
    event EventHandler? Changed;

    IReadOnlyList<ThemeProfile> Presets { get; }

    void SetTheme(AppTheme theme);

    /// <summary>Returns false if <paramref name="hex"/> isn't a valid colour.</summary>
    bool SetAccent(string hex);

    void SetFont(string familyName);

    /// <summary>Copies the image into Nexstrap's data folder and shows it. Returns an error message or null.</summary>
    string? SetBackgroundFromFile(string sourcePath);

    void ClearBackground();

    /// <summary>Live while dragging; persisted with a short delay.</summary>
    void SetBackgroundOpacity(double opacity);

    void SetBackgroundFit(BackgroundFit fit);

    void SetBackgroundAnchor(BackgroundAnchor anchor);

    void ApplyProfile(ThemeProfile profile);

    IReadOnlyList<ThemeProfile> LoadSavedProfiles();

    bool ProfileExists(string name);

    ThemeProfile SaveProfile(string name);

    void DeleteProfile(ThemeProfile profile);

    /// <summary>Validates and stores a profile file. Throws <see cref="InvalidDataException"/> on bad input.</summary>
    ThemeProfile ImportProfile(string path);

    void ExportProfile(ThemeProfile profile, string path);

    void Reset();
}

/// <summary>
/// Owns appearance state: validates input, applies it through <see cref="IThemeService"/>,
/// persists it, and manages presets plus user-saved theme profiles (Themes\*.json).
/// </summary>
public sealed class AppearanceService : IAppearanceService
{
    public static readonly string[] SupportedImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILogger<AppearanceService> _logger;
    private readonly string _themesDir;
    private readonly string _backgroundsDir;

    public AppearanceService(ISettingsService settings, IThemeService theme, ILogger<AppearanceService> logger)
        : this(settings, theme, logger, AppInfo.Paths.Themes, AppInfo.Paths.Backgrounds) { }

    internal AppearanceService(ISettingsService settings, IThemeService theme, ILogger<AppearanceService> logger,
        string themesDir, string backgroundsDir)
    {
        _settings = settings;
        _theme = theme;
        _logger = logger;
        _themesDir = themesDir;
        _backgroundsDir = backgroundsDir;
    }

    public AppearanceSettings Current => _settings.Current.Appearance;

    public event EventHandler? Changed;

    public IReadOnlyList<ThemeProfile> Presets { get; } =
    [
        Preset("Nexstrap", AppTheme.Dark, "#7C6CFF"),
        Preset("Glacier", AppTheme.Dark, "#38BDF8"),
        Preset("Ember", AppTheme.Dark, "#F97316"),
        Preset("Verdant", AppTheme.Dark, "#22C55E"),
        Preset("Rose", AppTheme.Dark, "#F43F5E"),
        Preset("Daylight", AppTheme.Light, "#6366F1"),
        Preset("Paper", AppTheme.Light, "#0F766E"),
    ];

    private static ThemeProfile Preset(string name, AppTheme theme, string accent) =>
        new() { Name = name, IsPreset = true, Appearance = new AppearanceSettings { Theme = theme, AccentColor = accent } };

    // ===== Individual values =====

    public void SetTheme(AppTheme theme)
    {
        Current.Theme = theme;
        _theme.ApplyTheme(theme);
        Commit();
    }

    public bool SetAccent(string hex)
    {
        if (!ColorUtil.TryParseHex(hex, out var color)) return false;
        color.A = 255;
        Current.AccentColor = ColorUtil.ToHex(color);
        _theme.ApplyAccent(color);
        Commit();
        return true;
    }

    public void SetFont(string familyName)
    {
        Current.FontFamily = string.IsNullOrWhiteSpace(familyName) ? AppearanceSettings.DefaultFont : familyName.Trim();
        _theme.ApplyFont(Current.FontFamily);
        Commit();
    }

    public string? SetBackgroundFromFile(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!SupportedImageExtensions.Contains(ext))
            return Loc.Format("Appearance.FilesArentSupportedUsePNGJPG", ext);
        if (!File.Exists(sourcePath))
            return Loc.Get("Appearance.ThatFileNoLongerExists");

        string target;
        try
        {
            Directory.CreateDirectory(_backgroundsDir);
            // Content-addressed name: re-choosing the same image doesn't duplicate it.
            using (var stream = File.OpenRead(sourcePath))
                target = Path.Combine(_backgroundsDir, Convert.ToHexString(SHA256.HashData(stream))[..16].ToLowerInvariant() + ext);
            if (!File.Exists(target))
                File.Copy(sourcePath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to copy background {Path}", sourcePath);
            return Loc.Get("Appearance.NexstrapCouldntCopyThatImage") + ex.Message;
        }

        var previous = Current.BackgroundImage;
        Current.BackgroundImage = target;
        if (!_theme.ApplyBackground(Current))
        {
            Current.BackgroundImage = previous;
            _theme.ApplyBackground(Current);
            TryDeleteUnused(target);
            return Loc.Get("Appearance.ThatFileCouldntBeReadAs");
        }

        Commit();
        if (!string.Equals(previous, target, StringComparison.OrdinalIgnoreCase))
            TryDeleteUnused(previous);
        return null;
    }

    public void ClearBackground()
    {
        var previous = Current.BackgroundImage;
        Current.BackgroundImage = null;
        _theme.ApplyBackground(Current);
        Commit();
        TryDeleteUnused(previous);
    }

    public void SetBackgroundOpacity(double opacity)
    {
        Current.BackgroundOpacity = Math.Round(Math.Clamp(opacity, 0, 1), 2);
        _theme.ApplyBackground(Current);
        _settings.SaveDeferred();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetBackgroundFit(BackgroundFit fit)
    {
        Current.BackgroundFit = fit;
        _theme.ApplyBackground(Current);
        Commit();
    }

    public void SetBackgroundAnchor(BackgroundAnchor anchor)
    {
        Current.BackgroundAnchor = anchor;
        _theme.ApplyBackground(Current);
        Commit();
    }

    // ===== Profiles =====

    public void ApplyProfile(ThemeProfile profile)
    {
        if (profile.IsPreset)
        {
            // Presets are colour schemes: keep the user's font and background.
            Current.Theme = profile.Appearance.Theme;
            Current.AccentColor = profile.Appearance.AccentColor;
        }
        else
        {
            var previousBackground = Current.BackgroundImage;
            Current.CopyFrom(Sanitise(profile.Appearance));
            if (!string.Equals(previousBackground, Current.BackgroundImage, StringComparison.OrdinalIgnoreCase))
                TryDeleteUnused(previousBackground);
        }

        _theme.Apply(Current);
        Commit();
        _logger.LogInformation("Applied theme profile {Name}", profile.Name);
    }

    public IReadOnlyList<ThemeProfile> LoadSavedProfiles()
    {
        if (!Directory.Exists(_themesDir)) return [];

        var profiles = new List<ThemeProfile>();
        foreach (var file in Directory.EnumerateFiles(_themesDir, "*.json"))
        {
            try
            {
                var profile = Parse(File.ReadAllText(file));
                profile.FilePath = file;
                profiles.Add(profile);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Skipping unreadable theme {File}", file);
            }
        }
        return profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public bool ProfileExists(string name) => File.Exists(ProfilePath(name));

    public ThemeProfile SaveProfile(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException(Loc.Get("Appearance.AThemeNeedsAName"), nameof(name));

        var profile = new ThemeProfile { Name = name, Appearance = Current.Clone(), FilePath = ProfilePath(name) };
        Directory.CreateDirectory(_themesDir);
        File.WriteAllText(profile.FilePath, JsonSerializer.Serialize(profile, JsonFileStore<ThemeProfile>.SerializerOptions));
        _logger.LogInformation("Saved theme profile {Name}", name);
        return profile;
    }

    public void DeleteProfile(ThemeProfile profile)
    {
        if (profile.IsPreset || profile.FilePath is null) return;
        File.Delete(profile.FilePath);
        TryDeleteUnused(profile.Appearance.BackgroundImage);
    }

    public ThemeProfile ImportProfile(string path)
    {
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(Loc.Get("Appearance.TheFileCouldntBeRead") + ex.Message, ex);
        }

        var profile = Parse(json);
        profile.FilePath = ProfilePath(profile.Name);
        Directory.CreateDirectory(_themesDir);
        File.WriteAllText(profile.FilePath, JsonSerializer.Serialize(profile, JsonFileStore<ThemeProfile>.SerializerOptions));
        return profile;
    }

    public void ExportProfile(ThemeProfile profile, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonFileStore<ThemeProfile>.SerializerOptions));

    public void Reset()
    {
        var previous = Current.BackgroundImage;
        Current.CopyFrom(new AppearanceSettings());
        _theme.Apply(Current);
        Commit();
        TryDeleteUnused(previous);
        _logger.LogInformation("Appearance reset to defaults");
    }

    // ===== Helpers =====

    /// <summary>Parses and validates profile JSON, repairing out-of-range values.</summary>
    internal static ThemeProfile Parse(string json)
    {
        ThemeProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<ThemeProfile>(json, JsonFileStore<ThemeProfile>.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Loc.Get("Appearance.ThisIsntAValidNexstrapTheme"), ex);
        }

        if (profile is null || string.IsNullOrWhiteSpace(profile.Name))
            throw new InvalidDataException(Loc.Get("Appearance.TheThemeFileHasNoName"));

        profile.Name = profile.Name.Trim();
        profile.Appearance = Sanitise(profile.Appearance ?? new AppearanceSettings());
        return profile;
    }

    private static AppearanceSettings Sanitise(AppearanceSettings a)
    {
        var clean = a.Clone();
        if (!ColorUtil.TryParseHex(clean.AccentColor, out _)) clean.AccentColor = AppearanceSettings.DefaultAccent;
        if (string.IsNullOrWhiteSpace(clean.FontFamily)) clean.FontFamily = AppearanceSettings.DefaultFont;
        if (!Enum.IsDefined(clean.Theme)) clean.Theme = AppTheme.Dark;
        if (!Enum.IsDefined(clean.BackgroundFit)) clean.BackgroundFit = BackgroundFit.Fill;
        if (!Enum.IsDefined(clean.BackgroundAnchor)) clean.BackgroundAnchor = BackgroundAnchor.Center;
        clean.BackgroundOpacity = double.IsFinite(clean.BackgroundOpacity) ? Math.Clamp(clean.BackgroundOpacity, 0, 1) : 0.35;
        // A background from another PC won't exist here.
        if (clean.BackgroundImage is not null && !File.Exists(clean.BackgroundImage)) clean.BackgroundImage = null;
        return clean;
    }

    internal string ProfilePath(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(_themesDir, safe + ".json");
    }

    /// <summary>Deletes a copied background once neither the settings nor any saved profile use it.</summary>
    private void TryDeleteUnused(string? image)
    {
        if (string.IsNullOrEmpty(image)) return;
        try
        {
            var full = Path.GetFullPath(image);
            if (!full.StartsWith(Path.GetFullPath(_backgroundsDir), StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(Current.BackgroundImage, image, StringComparison.OrdinalIgnoreCase)) return;
            if (LoadSavedProfiles().Any(p => string.Equals(p.Appearance.BackgroundImage, image, StringComparison.OrdinalIgnoreCase))) return;
            File.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete old background {Path}", image);
        }
    }

    private void Commit()
    {
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
