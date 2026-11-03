using System.IO;
using Nexstrap.Core;

namespace Nexstrap.Services.Roblox;

public interface IGameShortcutService
{
    /// <summary>Creates a desktop shortcut that joins the game. Returns the file created.</summary>
    string CreateOnDesktop(long placeId, string name);
}

/// <summary>
/// Internet shortcuts (.url) opening Roblox's join link, so they go through whichever
/// program handles roblox: links. Existing files are never overwritten.
/// </summary>
public sealed class GameShortcutService : IGameShortcutService
{
    private readonly string _folder;
    private readonly string? _iconPath;

    public GameShortcutService()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.ProcessPath) { }

    internal GameShortcutService(string folder, string? iconPath)
    {
        _folder = folder;
        _iconPath = iconPath;
    }

    public string CreateOnDesktop(long placeId, string name)
    {
        var baseName = SafeFileName(name);
        var path = Path.Combine(_folder, baseName + ".url");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(_folder, $"{baseName} ({i}).url");

        var lines = new List<string> { "[InternetShortcut]", "URL=" + RobloxLinks.PlaceLaunchUri(placeId) };
        if (_iconPath is not null)
        {
            lines.Add("IconFile=" + _iconPath);
            lines.Add("IconIndex=0");
        }
        File.WriteAllLines(path, lines);
        return path;
    }

    internal static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (safe.Length > 80) safe = safe[..80].TrimEnd(' ', '.');
        return safe.Length == 0 ? "Roblox game" : safe;
    }
}
