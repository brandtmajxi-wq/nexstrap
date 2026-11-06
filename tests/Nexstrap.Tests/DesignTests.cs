using System.Text.Json;
using System.Windows.Media;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Xunit;

namespace Nexstrap.Tests;

public class DesignTests
{
    private static readonly Color Dark = Color.FromRgb(0x12, 0x13, 0x18);
    private static readonly Color Accent = Color.FromRgb(0x7C, 0x6C, 0xFF);

    [Fact]
    public void Defaults_keep_the_look_people_already_have()
    {
        var a = new AppearanceSettings();

        Assert.Equal(new Radii(12, 8, 6, 10), DesignTokens.RadiiFor(a.Corners));
        Assert.Null(DesignTokens.CardAlpha(a.Cards, hasBackground: false));
        Assert.Equal((byte)0xD0, DesignTokens.CardAlpha(a.Cards, hasBackground: true));
        Assert.Null(DesignTokens.SidebarColor(Dark, Accent, a.SidebarTone, hasBackground: false));
        Assert.Equal(PageTransition.Slide, a.PageTransition);
        Assert.False(a.CompactSidebar);
        Assert.Equal(0, a.BackgroundBlur);
    }

    [Fact]
    public void Glass_and_outline_are_more_see_through_than_solid()
    {
        var solid = DesignTokens.CardAlpha(CardStyle.Solid, true)!.Value;
        var glass = DesignTokens.CardAlpha(CardStyle.Glass, true)!.Value;
        var outline = DesignTokens.CardAlpha(CardStyle.Outline, true)!.Value;

        Assert.True(solid > glass && glass > outline);
    }

    [Fact]
    public void An_accent_sidebar_is_tinted_but_stays_close_to_the_theme()
    {
        var tinted = DesignTokens.SidebarColor(Dark, Accent, SidebarTone.Accent, hasBackground: false)!.Value;

        Assert.Equal(255, tinted.A);
        Assert.True(tinted.B > Dark.B);
        Assert.True(tinted.B < Accent.B);
    }

    [Fact]
    public void Gradient_uses_the_chosen_colour_or_a_lighter_accent()
    {
        Assert.Equal(Color.FromRgb(0xFF, 0x4F, 0xD8), DesignTokens.GradientEnd(Accent, "#FF4FD8"));

        var automatic = DesignTokens.GradientEnd(Accent, null);
        Assert.True(automatic.R > Accent.R && automatic.G > Accent.G);
    }

    [Fact]
    public void Saved_themes_keep_every_design_choice()
    {
        var source = new AppearanceSettings
        {
            Corners = CornerStyle.Round, Cards = CardStyle.Glass, SidebarTone = SidebarTone.Accent, PageTransition = PageTransition.None,
            CompactSidebar = true, GradientButtons = true, GradientColor = "#FF4FD8", BackgroundBlur = 12, BrandName = "Liam", BrandLogo = "x", HideTagline = true
        };
        var copy = new AppearanceSettings();

        copy.CopyFrom(source);

        Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(copy));
    }

    [Fact]
    public void Shared_themes_are_cleaned_up()
    {
        var profile = AppearanceService.Parse("""
            { "Name": "Bad", "Appearance": { "BrandName": "H1tler", "BrandLogo": "C:\\nope\\missing.png", "GradientColor": "green", "BackgroundBlur": 900, "Corners": 99 } }
            """);

        Assert.Null(profile.Appearance.BrandName);
        Assert.Null(profile.Appearance.BrandLogo);
        Assert.Null(profile.Appearance.GradientColor);
        Assert.Equal(DesignTokens.MaxBlur, profile.Appearance.BackgroundBlur);
        Assert.Equal(CornerStyle.Normal, profile.Appearance.Corners);
    }
}
