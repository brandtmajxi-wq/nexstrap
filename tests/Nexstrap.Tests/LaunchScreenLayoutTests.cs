using System.Windows;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Xunit;

namespace Nexstrap.Tests;

public class LaunchScreenLayoutTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1040);
    private static readonly Size Window = new(520, 300);

    [Theory]
    [InlineData(LaunchPosition.Center, 700, 370)]
    [InlineData(LaunchPosition.TopLeft, 24, 24)]
    [InlineData(LaunchPosition.TopRight, 1376, 24)]
    [InlineData(LaunchPosition.BottomLeft, 24, 716)]
    [InlineData(LaunchPosition.BottomRight, 1376, 716)]
    public void Places_the_window_inside_the_screen(LaunchPosition position, double x, double y) =>
        Assert.Equal(new Point(x, y), LaunchScreenLayout.Place(Screen, Window, position));

    [Fact]
    public void Follows_a_screen_that_doesnt_start_at_zero()
    {
        var place = LaunchScreenLayout.Place(new Rect(100, 40, 1000, 800), Window, LaunchPosition.TopLeft);

        Assert.Equal(new Point(124, 64), place);
    }

    [Fact]
    public void Defaults_look_like_the_old_loading_screen()
    {
        var options = new LaunchScreenSettings();

        Assert.Equal(new Size(520, 300), LaunchScreenLayout.WindowSize(options.WindowSize));
        Assert.Equal(16, LaunchScreenLayout.CornerRadius(options.Corners));
        Assert.Equal(20, LaunchScreenLayout.TitleSize(options.TextSize));
        Assert.Equal(1, LaunchScreenLayout.SpeedFactor(options.Speed));
        Assert.Equal(0.45, options.ImageOpacity);
        Assert.Equal(0, options.MinimumSeconds);
    }

    [Fact]
    public void Slow_takes_longer_and_fast_takes_less() =>
        Assert.True(LaunchScreenLayout.SpeedFactor(LaunchSpeed.Slow) > 1 && LaunchScreenLayout.SpeedFactor(LaunchSpeed.Fast) < 1);

    [Theory]
    [InlineData(true, false, 1, 3, false)] // Roblox is ready, but the minimum hasn't passed
    [InlineData(true, false, 3, 3, true)]
    [InlineData(false, true, 1, 3, true)]  // Roblox closed: never wait
    [InlineData(false, false, 20, 30, true)] // the timeout always wins
    public void The_minimum_time_keeps_the_screen_up_only_while_Roblox_is_running(bool hasWindow, bool exited, double seconds, double minimum, bool expected) =>
        Assert.Equal(expected, LaunchScreenService.ShouldClose(hasWindow, exited, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(minimum)));
}
