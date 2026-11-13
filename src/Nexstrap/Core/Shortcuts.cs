using System.Windows.Input;

namespace Nexstrap.Core;

public enum ShortcutAction { None, Page, Play, Rejoin, Search, Notifications }

/// <summary>A shortcut as the About page lists it.</summary>
public sealed record ShortcutInfo(string Keys, string Description);

/// <summary>Keyboard shortcuts inside the Nexstrap window. The global ones (Ctrl+Alt+N/J) live in HotkeyService.</summary>
public static class Shortcuts
{
    /// <summary>What a key press does: an action, and for Page which sidebar position (1–9).</summary>
    public static (ShortcutAction Action, int Page) Match(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.Control) return (ShortcutAction.None, 0);
        if (key is >= Key.D1 and <= Key.D9) return (ShortcutAction.Page, key - Key.D0);
        if (key is >= Key.NumPad1 and <= Key.NumPad9) return (ShortcutAction.Page, key - Key.NumPad0);
        return key switch
        {
            Key.P => (ShortcutAction.Play, 0),
            Key.J => (ShortcutAction.Rejoin, 0),
            Key.K => (ShortcutAction.Search, 0),
            Key.N => (ShortcutAction.Notifications, 0),
            _ => (ShortcutAction.None, 0)
        };
    }

    /// <summary>The list for the About page, in the current language ("Strg" instead of "Ctrl" in German).</summary>
    public static IReadOnlyList<ShortcutInfo> List()
    {
        var ctrl = Loc.Get("Shortcut.Ctrl");
        return
        [
            new($"{ctrl}+1 … {ctrl}+9", Loc.Get("Shortcut.Pages")),
            new($"{ctrl}+P", Loc.Get("Shortcut.Play")),
            new($"{ctrl}+J", Loc.Get("Shortcut.Rejoin")),
            new($"{ctrl}+K", Loc.Get("Shortcut.Search")),
            new($"{ctrl}+N", Loc.Get("Shortcut.Notifications"))
        ];
    }
}
