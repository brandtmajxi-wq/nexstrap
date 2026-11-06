using System.Windows.Media;
using Nexstrap.Models;

namespace Nexstrap.Core;

public readonly record struct Radii(double Card, double Control, double Small, double Large);

/// <summary>Turns the design choices on the Appearance page into sizes and colours.</summary>
public static class DesignTokens
{
    public const double FullSidebar = 236;
    public const double IconSidebar = 72;
    public const int BrandNameLimit = 20;
    public const double MaxBlur = 30;

    private const byte OverBackground = 0xD0;

    public static Radii RadiiFor(CornerStyle style) => style switch
    {
        CornerStyle.Sharp => new Radii(0, 0, 0, 0),
        CornerStyle.Round => new Radii(22, 14, 10, 18),
        _ => new Radii(12, 8, 6, 10)
    };

    /// <summary>How solid cards are, or null to use the theme's colour as it is.</summary>
    public static byte? CardAlpha(CardStyle style, bool hasBackground) => style switch
    {
        CardStyle.Glass => hasBackground ? (byte)0xA8 : (byte)0xB0,
        CardStyle.Outline => hasBackground ? (byte)0x78 : (byte)0x30,
        _ => hasBackground ? OverBackground : null
    };

    /// <summary>The sidebar's colour, or null to use the theme's colour as it is.</summary>
    public static Color? SidebarColor(Color theme, Color accent, SidebarTone tone, bool hasBackground) => tone switch
    {
        SidebarTone.Accent => ColorUtil.WithAlpha(Blend(theme, accent, 0.18), hasBackground ? OverBackground : (byte)0xFF),
        SidebarTone.Clear => ColorUtil.WithAlpha(theme, hasBackground ? (byte)0x50 : (byte)0x70),
        _ => hasBackground ? ColorUtil.WithAlpha(theme, OverBackground) : null
    };

    public static Color GradientEnd(Color accent, string? chosen) =>
        ColorUtil.TryParseHex(chosen, out var color) ? color : Blend(accent, Colors.White, 0.35);

    public static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
}
