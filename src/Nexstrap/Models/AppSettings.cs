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

    public LaunchScreenSettings LaunchScreen { get; set; } = new();

    /// <summary>Experiences pinned on the Games page.</summary>
    public List<RecentExperience> FavoriteExperiences { get; set; } = [];

    public NotificationSettings Notifications { get; set; } = new();

    public RobloxFontSettings RobloxFont { get; set; } = new();

    public RobloxModSettings RobloxMods { get; set; } = new();

    public SidebarSettings Sidebar { get; set; } = new();

    public WinterSettings Winter { get; set; } = new();

    public WelcomeSettings Welcome { get; set; } = new();

    /// <summary>Per-game frame-rate and graphics choices.</summary>
    public List<GameProfile> GameProfiles { get; set; } = [];

    /// <summary>Language code; null follows Windows.</summary>
    public string? Language { get; set; }

    /// <summary>The Moscow page has been opened with its password on this PC.</summary>
    public bool SecretRoomUnlocked { get; set; }

    /// <summary>Set by "Run without installing" so setup doesn't appear on every start.</summary>
    public bool SkippedSetup { get; set; }
}

public sealed class GameProfile
{
    public long PlaceId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Null keeps the normal setting.</summary>
    public int? FramerateCap { get; set; }

    /// <summary>0 is automatic, 1-10 the graphics slider. Null keeps the normal setting.</summary>
    public int? Quality { get; set; }
}

/// <summary>Roblox settings to put back once a game with a profile has ended.</summary>
public sealed class ProfileRestore
{
    public int? FramerateCap { get; set; }
    public int? Quality { get; set; }
}

public sealed class WelcomeSettings
{
    /// <summary>What the Welcome page calls you. Stays on this PC.</summary>
    public string? Name { get; set; }

    /// <summary>Set once the question has been shown, so it only appears one time.</summary>
    public bool NameAsked { get; set; }

    /// <summary>Moscow behind the Welcome page. Only used once the secret room is unlocked, which turns it on.</summary>
    public bool MoscowScene { get; set; }
}

public sealed class WinterSettings
{
    public Nexstrap.Core.WinterMode Mode { get; set; } = Nexstrap.Core.WinterMode.Automatic;

    public bool Snow { get; set; } = true;

    /// <summary>Dev only: winter theme and the Christmas greeting whatever the date.</summary>
    public bool ChristmasMode { get; set; }
}

/// <summary>Page names in the chosen order, and the ones to hide. Empty means the default sidebar.</summary>
public sealed class SidebarSettings
{
    public List<string> Order { get; set; } = [];

    public List<string> Hidden { get; set; } = [];
}

/// <summary>Opt-in replacement of Roblox's interface fonts in Nexstrap-installed builds.</summary>
public sealed class RobloxFontSettings
{
    public bool Enabled { get; set; }

    /// <summary>Copy of the chosen .ttf/.otf in Nexstrap's Fonts folder.</summary>
    public string? FontFile { get; set; }

    /// <summary>Name shown in the UI.</summary>
    public string? DisplayName { get; set; }
}

/// <summary>Opt-in replacements for Roblox's default death sound and mouse cursor. A null file means Roblox's own.</summary>
public sealed class RobloxModSettings
{
    /// <summary>Copy of the chosen .ogg in Nexstrap's Mods folder.</summary>
    public string? DeathSoundFile { get; set; }

    public string? DeathSoundName { get; set; }

    /// <summary>Copy of the chosen .png in Nexstrap's Mods folder.</summary>
    public string? CursorFile { get; set; }

    public string? CursorName { get; set; }
}

/// <summary>Windows CPU priority for Roblox. Realtime is left out on purpose: it can freeze the PC.</summary>
public enum RobloxPriority
{
    Automatic,
    BelowNormal,
    AboveNormal,
    High
}

public sealed class NotificationSettings
{
    public bool RobloxUpdates { get; set; } = true;
    public bool NexstrapUpdates { get; set; } = true;
    public bool ConfigurationWarnings { get; set; } = true;

    /// <summary>Windows pop-up when a game finishes loading while another app is in front.</summary>
    public bool GameJoinAlerts { get; set; } = true;

    /// <summary>Offer to rejoin or open the logs when Roblox stops suddenly.</summary>
    public bool CrashAlerts { get; set; } = true;

    /// <summary>A note over Roblox each time this much play time has passed; 0 is off.</summary>
    public int PlayReminderMinutes { get; set; }

    /// <summary>Keys of dismissed notifications. Keys include the version or count, so new ones show again.</summary>
    public List<string> Dismissed { get; set; } = [];
}

public enum LaunchAnimation
{
    Spinner,
    Bar,
    Pulse,
    None,
    Dots
}

public enum LaunchSpeed { Slow, Normal, Fast }

public enum LaunchTextSize { Small, Medium, Large }

public enum LaunchWindowSize { Small, Medium, Large }

public enum LaunchPosition { Center, TopLeft, TopRight, BottomLeft, BottomRight }

public enum LaunchCorners { Square, Rounded, VeryRounded }

public enum LaunchLogo { Nexstrap, Custom, Hidden }

public enum LaunchImageFit { Fill, Fit }

/// <summary>The window shown while Roblox starts.</summary>
public sealed class LaunchScreenSettings
{
    public bool Enabled { get; set; } = true;
    public string Title { get; set; } = "Starting Roblox";
    public string BackgroundColor { get; set; } = "#0E0F13";
    public string AccentColor { get; set; } = "#7C6CFF";
    public LaunchAnimation Animation { get; set; } = LaunchAnimation.Spinner;

    /// <summary>Copy in Nexstrap's Backgrounds folder; null for a plain colour.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>Optional smaller text under the title.</summary>
    public string? Subtitle { get; set; }

    /// <summary>Null picks black or white, whichever is easier to read.</summary>
    public string? TextColor { get; set; }

    public LaunchTextSize TextSize { get; set; } = LaunchTextSize.Medium;
    public LaunchSpeed Speed { get; set; } = LaunchSpeed.Normal;
    public LaunchWindowSize WindowSize { get; set; } = LaunchWindowSize.Medium;
    public LaunchPosition Position { get; set; } = LaunchPosition.Center;
    public LaunchCorners Corners { get; set; } = LaunchCorners.Rounded;
    public LaunchLogo Logo { get; set; } = LaunchLogo.Nexstrap;

    /// <summary>Copy in Nexstrap's Backgrounds folder.</summary>
    public string? LogoImage { get; set; }

    public LaunchImageFit ImageFit { get; set; } = LaunchImageFit.Fill;
    public double ImageOpacity { get; set; } = 0.45;
    public bool ImageBlur { get; set; }
    public bool Fade { get; set; } = true;

    /// <summary>Stays up at least this long after opening, even if Roblox is faster.</summary>
    public int MinimumSeconds { get; set; }
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
    public const string DefaultMenuTopLine = "Inside Nexstrap";
    public const string DefaultMenuBottomLine = "Browsing Roblox";

    /// <summary>First line of the card while in the Roblox menu.</summary>
    public string MenuTopLine { get; set; } = DefaultMenuTopLine;

    /// <summary>Second line of the card while in the Roblox menu.</summary>
    public string MenuBottomLine { get; set; } = DefaultMenuBottomLine;

    /// <summary>Card icon: an https image link or a Discord Art Asset name. Empty uses the built-in logo.</summary>
    public string? MenuImage { get; set; }

    /// <summary>Show what you're doing in Roblox on your Discord profile while Nexstrap is running.</summary>
    public bool RichPresence { get; set; } = true;

    /// <summary>Include the experience name and icon; off shows only "In a Roblox experience".</summary>
    public bool ShowExperienceDetails { get; set; } = true;

    /// <summary>Add a "View game" button linking to the game page.</summary>
    public bool ShowExperienceButton { get; set; } = true;

    /// <summary>Show your avatar headshot and username in the corner of the game icon.</summary>
    public bool ShowAvatar { get; set; } = true;

    /// <summary>Add the server's city to the in-game card. Off by default: it says roughly where you're playing.</summary>
    public bool ShowServerLocation { get; set; }

    /// <summary>Overrides the built-in Discord application ID (advanced).</summary>
    public string? ApplicationIdOverride { get; set; }
}

public sealed class LauncherSettings
{
    /// <summary>Close Nexstrap once Roblox has been started.</summary>
    public bool CloseAfterLaunch { get; set; }

    /// <summary>Query GitHub for a newer Nexstrap when the app opens.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    public bool GlobalHotkeys { get; set; }

    public bool AutoUpdateRoblox { get; set; } = true;

    public ProfileRestore? ProfileRestore { get; set; }

    public RobloxPriority RobloxPriority { get; set; }

    /// <summary>How many CPU cores Roblox may use; 0 means all.</summary>
    public int RobloxCoreLimit { get; set; }

    /// <summary>Title for Roblox's window; empty keeps Roblox's own.</summary>
    public string? RobloxWindowTitle { get; set; }

    /// <summary>Count time spent in games (stored in History.json).</summary>
    public bool RecordPlayHistory { get; set; } = true;

    /// <summary>The window's X hides Nexstrap to the tray instead of closing it.</summary>
    public bool CloseToTray { get; set; }

    /// <summary>When opened by Windows at sign-in, stay in the tray.</summary>
    public bool StartHidden { get; set; }

    public LastServerInfo? LastServer { get; set; }

    /// <summary>Delete old logs and temp files each time Nexstrap opens.</summary>
    public bool CleanOldFilesOnStart { get; set; }

    /// <summary>Install directory Play uses; null picks automatically (see RobloxClientService).</summary>
    public string? PreferredInstallation { get; set; }

    /// <summary>Most recently launched experiences, newest first.</summary>
    public List<RecentExperience> RecentExperiences { get; set; } = [];

    /// <summary>URL handler commands that were registered before Nexstrap took over (scheme → command).</summary>
    public Dictionary<string, string> PreviousProtocolHandlers { get; set; } = [];
}

/// <summary>The server most recently played on, for rejoining it.</summary>
public sealed class LastServerInfo
{
    public long PlaceId { get; set; }
    public string JobId { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>When you joined, or left if you've left since.</summary>
    public DateTime SeenUtc { get; set; }
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

public enum CornerStyle { Normal, Sharp, Round }

public enum CardStyle { Solid, Glass, Outline }

public enum SidebarTone { Default, Accent, Clear }

public enum PageTransition { Slide, Fade, None }

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

    public CornerStyle Corners { get; set; } = CornerStyle.Normal;
    public CardStyle Cards { get; set; } = CardStyle.Solid;
    public SidebarTone SidebarTone { get; set; } = SidebarTone.Default;
    public PageTransition PageTransition { get; set; } = PageTransition.Slide;
    public bool CompactSidebar { get; set; }
    public bool GradientButtons { get; set; }

    /// <summary>Second gradient colour; null uses a lighter accent.</summary>
    public string? GradientColor { get; set; }

    /// <summary>Blur radius for the background image; 0 is off.</summary>
    public double BackgroundBlur { get; set; }

    /// <summary>Shown at the top of the sidebar instead of Nexstrap.</summary>
    public string? BrandName { get; set; }

    /// <summary>Copy in the Backgrounds folder, shown instead of the Nexstrap logo.</summary>
    public string? BrandLogo { get; set; }

    public bool HideTagline { get; set; }

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
        Corners = other.Corners;
        Cards = other.Cards;
        SidebarTone = other.SidebarTone;
        PageTransition = other.PageTransition;
        CompactSidebar = other.CompactSidebar;
        GradientButtons = other.GradientButtons;
        GradientColor = other.GradientColor;
        BackgroundBlur = other.BackgroundBlur;
        BrandName = other.BrandName;
        BrandLogo = other.BrandLogo;
        HideTagline = other.HideTagline;
    }
}

public sealed class WindowSettings
{
    public double Width { get; set; } = 1120;
    public double Height { get; set; } = 720;
    public bool Maximized { get; set; }

    /// <summary>Size of everything in the main window; 1 is normal.</summary>
    public double Scale { get; set; } = 1;
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
