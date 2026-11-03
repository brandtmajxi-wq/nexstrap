namespace Nexstrap.Models;

/// <summary>
/// Root of the persisted user configuration (Settings.json).
/// New sections are added as features land; unknown/missing properties fall back to defaults.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Schema version so future releases can migrate old files.</summary>
    public int SchemaVersion { get; set; } = 1;

    public AppearanceSettings Appearance { get; set; } = new();

    public WindowSettings Window { get; set; } = new();

    public LauncherSettings Launcher { get; set; } = new();

    public DiscordSettings Discord { get; set; } = new();

    /// <summary>Programs started alongside Roblox (e.g. a recorder or voice app).</summary>
    public List<CompanionApp> CompanionApps { get; set; } = [];
}

public sealed class CompanionApp
{
    public string Name { get; set; } = "";

    /// <summary>Full path to an .exe the user picked.</summary>
    public string Path { get; set; } = "";

    public string Arguments { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Ask the app to close (like clicking its X) when Roblox exits.</summary>
    public bool CloseWithRoblox { get; set; }
}

public sealed class DiscordSettings
{
    /// <summary>Show what you're doing in Roblox on your Discord profile while Nexstrap is running.</summary>
    public bool RichPresence { get; set; } = true;

    /// <summary>Include the experience name and icon; off shows only "In a Roblox experience".</summary>
    public bool ShowExperienceDetails { get; set; } = true;

    /// <summary>Add a "View game" button linking to the game page.</summary>
    public bool ShowExperienceButton { get; set; } = true;

    /// <summary>Show your avatar headshot and username in the corner of the game icon.</summary>
    public bool ShowAvatar { get; set; } = true;

    /// <summary>Overrides the built-in Discord application ID (advanced).</summary>
    public string? ApplicationIdOverride { get; set; }
}

public sealed class LauncherSettings
{
    /// <summary>Close Nexstrap once Roblox has been started.</summary>
    public bool CloseAfterLaunch { get; set; }

    /// <summary>Query GitHub for a newer Nexstrap when the app opens.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>Install directory Play uses; null picks automatically (see RobloxClientService).</summary>
    public string? PreferredInstallation { get; set; }

    /// <summary>Most recently launched experiences, newest first.</summary>
    public List<RecentExperience> RecentExperiences { get; set; } = [];

    /// <summary>URL handler commands that were registered before Nexstrap took over (scheme → command).</summary>
    public Dictionary<string, string> PreviousProtocolHandlers { get; set; } = [];
}

public sealed class RecentExperience
{
    public long PlaceId { get; set; }
    public string Name { get; set; } = "";
    public DateTime LastLaunched { get; set; }
}

public enum AppTheme
{
    Dark,
    Light
}

/// <summary>How the background image fills the content area.</summary>
public enum BackgroundFit
{
    /// <summary>Cover the whole area, cropping as needed.</summary>
    Fill,
    /// <summary>Show the whole image, letterboxed.</summary>
    Fit,
    /// <summary>Distort to exactly match the area.</summary>
    Stretch,
    /// <summary>Original size.</summary>
    Center,
    /// <summary>Repeat at original size.</summary>
    Tile
}

/// <summary>Which part of the image stays visible when it is cropped or smaller than the area.</summary>
public enum BackgroundAnchor
{
    Center,
    Top,
    Bottom,
    Left,
    Right
}

public sealed class AppearanceSettings
{
    public const string DefaultFont = "Segoe UI Variable Display";
    public const string DefaultAccent = "#7C6CFF";

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Accent colour as #RRGGBB.</summary>
    public string AccentColor { get; set; } = DefaultAccent;

    /// <summary>Font family name used across the whole UI.</summary>
    public string FontFamily { get; set; } = DefaultFont;

    /// <summary>Image copied into Nexstrap's Backgrounds folder; null for none.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>0..1</summary>
    public double BackgroundOpacity { get; set; } = 0.35;

    public BackgroundFit BackgroundFit { get; set; } = BackgroundFit.Fill;

    public BackgroundAnchor BackgroundAnchor { get; set; } = BackgroundAnchor.Center;

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();

    /// <summary>Copies every value from <paramref name="other"/> into this instance.</summary>
    public void CopyFrom(AppearanceSettings other)
    {
        Theme = other.Theme;
        AccentColor = other.AccentColor;
        FontFamily = other.FontFamily;
        BackgroundImage = other.BackgroundImage;
        BackgroundOpacity = other.BackgroundOpacity;
        BackgroundFit = other.BackgroundFit;
        BackgroundAnchor = other.BackgroundAnchor;
    }
}

public sealed class WindowSettings
{
    public double Width { get; set; } = 1120;
    public double Height { get; set; } = 720;
    public bool Maximized { get; set; }
}

/// <summary>A named appearance, either built in or saved by the user (Themes\*.json).</summary>
public sealed class ThemeProfile
{
    public string Name { get; set; } = "";

    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>Built-in presets only change theme + accent; saved profiles restore everything.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPreset { get; set; }

    /// <summary>File the profile was loaded from (saved profiles only).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FilePath { get; set; }
}
