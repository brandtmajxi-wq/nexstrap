using System.Windows;
using Nexstrap.Models;

namespace Nexstrap.Core;

/// <summary>Turns the loading screen's choices into sizes, positions and timings.</summary>
public static class LaunchScreenLayout
{
    private const double EdgeGap = 24;

    public static Size WindowSize(LaunchWindowSize size) => size switch
    {
        LaunchWindowSize.Small => new Size(420, 240),
        LaunchWindowSize.Large => new Size(680, 400),
        _ => new Size(520, 300)
    };

    public static double CornerRadius(LaunchCorners corners) => corners switch
    {
        LaunchCorners.Square => 0,
        LaunchCorners.VeryRounded => 32,
        _ => 16
    };

    public static double TitleSize(LaunchTextSize size) => size switch
    {
        LaunchTextSize.Small => 16,
        LaunchTextSize.Large => 26,
        _ => 20
    };

    /// <summary>How much longer or shorter one round of the animation takes.</summary>
    public static double SpeedFactor(LaunchSpeed speed) => speed switch
    {
        LaunchSpeed.Slow => 1.7,
        LaunchSpeed.Fast => 0.6,
        _ => 1
    };

    /// <summary>Where the window's top-left corner goes inside the screen's usable area.</summary>
    public static Point Place(Rect area, Size window, LaunchPosition position)
    {
        var left = area.Left + EdgeGap;
        var right = area.Right - window.Width - EdgeGap;
        var top = area.Top + EdgeGap;
        var bottom = area.Bottom - window.Height - EdgeGap;

        return position switch
        {
            LaunchPosition.TopLeft => new Point(left, top),
            LaunchPosition.TopRight => new Point(right, top),
            LaunchPosition.BottomLeft => new Point(left, bottom),
            LaunchPosition.BottomRight => new Point(right, bottom),
            _ => new Point(area.Left + (area.Width - window.Width) / 2, area.Top + (area.Height - window.Height) / 2)
        };
    }
}
