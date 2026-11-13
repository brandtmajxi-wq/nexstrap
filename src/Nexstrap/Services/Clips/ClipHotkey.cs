using System.Windows.Input;

namespace Nexstrap.Services.Clips;

/// <summary>The key combination that saves a clip, in the form Windows' RegisterHotKey uses.</summary>
public static class ClipHotkey
{
    public const uint Alt = 0x0001, Ctrl = 0x0002, Shift = 0x0004;

    public static readonly int[] LengthChoices = [30, 60, 90];

    /// <summary>"Ctrl+Alt+C", "F8", "Shift+F9"…</summary>
    public static string Format(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & Ctrl) != 0) parts.Add("Ctrl");
        if ((modifiers & Shift) != 0) parts.Add("Shift");
        if ((modifiers & Alt) != 0) parts.Add("Alt");
        parts.Add(KeyName(KeyInterop.KeyFromVirtualKey((int)virtualKey)));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
        Key.Oem3 => "`",
        Key.Next => "PageDown",
        Key.Prior => "PageUp",
        Key.Snapshot => "PrintScreen",
        _ => key.ToString(),
    };

    public static uint ToModifiers(ModifierKeys keys) =>
        ((keys & ModifierKeys.Alt) != 0 ? Alt : 0) | ((keys & ModifierKeys.Control) != 0 ? Ctrl : 0) | ((keys & ModifierKeys.Shift) != 0 ? Shift : 0);

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>
    /// A plain letter or number on its own would stop working while typing in every app, so those need Ctrl or Alt.
    /// Function keys (F1–F24) and keys like Insert or Pause can be used alone, the way clip apps usually do (F8).
    /// </summary>
    public static bool Allowed(uint modifiers, Key key)
    {
        if (IsModifier(key) || key is Key.None or Key.Escape or Key.Tab or Key.Enter or Key.Space or Key.Back) return false;
        if ((modifiers & (Ctrl | Alt)) != 0) return true;
        return key is >= Key.F1 and <= Key.F24 or Key.Insert or Key.Pause or Key.Scroll or Key.Home or Key.End or Key.PageUp or Key.PageDown
            or >= Key.NumPad0 and <= Key.NumPad9;
    }
}
