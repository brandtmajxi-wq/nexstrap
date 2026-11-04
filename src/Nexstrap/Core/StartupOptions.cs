namespace Nexstrap.Core;

/// <summary>
/// Command-line switches.
///   --capture &lt;file.png&gt;   render the main window to a PNG once loaded, then exit (UI regression checks)
///   --page &lt;Name&gt;          open a specific page on startup (e.g. Appearance)
///   --updated              set by the self-updater when it restarts Nexstrap
///   --uninstall            run by Windows' Installed apps list
/// </summary>
public sealed class StartupOptions
{
    public string? CapturePath { get; init; }

    public string? StartPage { get; init; }

    public bool JustUpdated { get; init; }

    public bool Uninstall { get; init; }

    /// <summary>Remaining arguments (e.g. a roblox-player: URI handed over by Windows).</summary>
    public IReadOnlyList<string> Positional { get; init; } = [];

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? capture = null, page = null;
        var updated = false;
        var uninstall = false;
        var rest = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--capture" when i + 1 < args.Count:
                    capture = args[++i];
                    break;
                case "--page" when i + 1 < args.Count:
                    page = args[++i];
                    break;
                case "--updated":
                    updated = true;
                    break;
                case "--uninstall":
                    uninstall = true;
                    break;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }

        return new StartupOptions { CapturePath = capture, StartPage = page, JustUpdated = updated, Uninstall = uninstall, Positional = rest };
    }
}
