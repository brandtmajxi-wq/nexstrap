using System.IO;
using System.Reflection;

namespace Nexstrap.Core;

/// <summary>
/// Static product identity and well-known file-system locations.
/// Everything Nexstrap writes lives under %LocalAppData%\Nexstrap.
/// </summary>
public static class AppInfo
{
    public const string Name = "Nexstrap";
    public const string Tagline = "The Roblox launcher, reimagined.";
    public const string DevLabel = "DEV Launcher";

    /// <summary>True only in the Dev configuration: a personal copy that runs beside the public one.</summary>
#if DEV_BUILD
    public static bool IsDev => true;
#else
    public static bool IsDev => false;
#endif

    public static string DisplayName => IsDev ? $"{Name} {DevLabel}" : Name;
    public const string RepositoryOwner = "brandtmajxi-wq";
    public const string RepositoryName = "nexstrap";
    public const string RepositoryUrl = $"https://github.com/{RepositoryOwner}/{RepositoryName}";

    /// <summary>
    /// Discord application that owns Nexstrap's Rich Presence (its name is the "Playing …" header,
    /// and it hosts the "nexstrap" art asset).
    /// </summary>
    public const string DiscordApplicationId = "1567244398654128138";

    /// <summary>Art asset key uploaded to the Discord application (branding/roblox-discord-1024.png).</summary>
    public const string DiscordLogoAsset = "roblox";

    public static Version Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Semantic version string without the trailing revision (e.g. "0.1.0").</summary>
    public static string VersionString => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    public static class Paths
    {
        /// <summary>Overridable root so tests can redirect all I/O to a temp folder.</summary>
        public static string Root { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), IsDev ? Name + " Dev" : Name);

        /// <summary>The dev copy keeps its own settings but shares Roblox builds, which are hundreds of MB.</summary>
        public static string Versions => IsDev
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name, "Versions")
            : Path.Combine(Root, "Versions");

        public static string Settings => Path.Combine(Root, "Settings.json");
        public static string Logs => Path.Combine(Root, "Logs");
        public static string Backups => Path.Combine(Root, "Backups");
        public static string Themes => Path.Combine(Root, "Themes");
        public static string Downloads => Path.Combine(Root, "Downloads");
        public static string Backgrounds => Path.Combine(Root, "Backgrounds");

        public static void EnsureCreated()
        {
            foreach (var dir in new[] { Root, Logs, Backups, Themes, Downloads, Backgrounds })
                Directory.CreateDirectory(dir);
        }
    }
}
