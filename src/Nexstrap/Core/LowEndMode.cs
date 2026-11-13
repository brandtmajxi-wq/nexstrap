using System.Windows;

namespace Nexstrap.Core;

/// <summary>
/// Low-end PC mode: Nexstrap stops everything that keeps the graphics card or processor busy while it's open
/// (weather and snow animation, page transitions, background blur). It overrides the look without changing
/// the Appearance settings, so turning it off brings everything back as it was.
/// </summary>
public static class LowEndMode
{
    public static bool On { get; private set; }

    /// <summary>Raised on the thread that changed it.</summary>
    public static event EventHandler? Changed;

    public static void Set(bool on)
    {
        if (On == on) return;
        On = on;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Whether moving effects should run: Windows' animation effects on, and low-end mode off.</summary>
    public static bool Animate => SystemParameters.ClientAreaAnimation && !On;
}
