namespace Nexstrap.Core;

/// <summary>
/// The new look, part by part. Each part is tried in the DEV Launcher first (<see cref="AppInfo.IsDev"/>) and
/// switched on for everyone when it's released.
/// </summary>
public static class NewLook
{
    /// <summary>Readable headings, setting cards with accent icons, the new sidebar and its play card.</summary>
    public static bool Styles => true; // released in 1.39.0

    /// <summary>Home: today/streak/all-time tiles and "Jump back in" picture tiles.</summary>
    public static bool HomeDashboard => true; // released in 1.40.0

    /// <summary>Games: picture cards and bars in Play history.</summary>
    public static bool Games => true; // released in 1.41.0

    /// <summary>Stats: icons, extra numbers, highlighted bars, ranks and the busiest-time pill.</summary>
    public static bool Stats => true; // released in 1.42.0

    /// <summary>Screenshots and Servers: hover cards and clearer server rows.</summary>
    public static bool ScreenshotsAndServers => true; // released in 1.43.0
}
