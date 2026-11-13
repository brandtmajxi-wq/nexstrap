using Nexstrap.Core;
using Nexstrap.Services.FastFlags;
using Nexstrap.Services.Performance;
using Xunit;

namespace Nexstrap.Tests;

[Collection("LowEndMode")] // the mode is app-wide, so these don't run alongside each other
public class LowEndModeTests
{
    [Fact]
    public void Turning_it_on_stops_animations_and_tells_listeners_once()
    {
        var changes = 0;
        void Count(object? s, EventArgs e) => changes++;
        LowEndMode.Changed += Count;
        try
        {
            LowEndMode.Set(true);
            LowEndMode.Set(true);
            Assert.True(LowEndMode.On);
            Assert.False(LowEndMode.Animate);
            Assert.Equal(1, changes);
        }
        finally
        {
            LowEndMode.Set(false);
            LowEndMode.Changed -= Count;
        }
        Assert.False(LowEndMode.On);
    }

    [Fact]
    public void It_doesnt_change_the_appearance_settings()
    {
        var settings = new Nexstrap.Models.AppSettings();
        settings.Appearance.BackgroundBlur = 12;
        settings.Appearance.PageTransition = Nexstrap.Models.PageTransition.Slide;
        LowEndMode.Set(true);
        LowEndMode.Set(false);
        Assert.Equal(12, settings.Appearance.BackgroundBlur);
        Assert.Equal(Nexstrap.Models.PageTransition.Slide, settings.Appearance.PageTransition);
    }

    [Fact]
    public void The_lighter_roblox_settings_it_offers_exist()
    {
        var saver = Assert.Single(PerformancePlanner.Presets, p => p.Key == "Performance.Preset.PowerSaver");
        Assert.Equal(60, saver.Cap);
        Assert.Single(FastFlagPresets.All, p => p.Key == "FastFlags.Preset.LowEnd");
    }

    [Fact]
    public void It_is_off_by_default() => Assert.False(new Nexstrap.Models.AppSettings().Launcher.LowEndMode);
}
