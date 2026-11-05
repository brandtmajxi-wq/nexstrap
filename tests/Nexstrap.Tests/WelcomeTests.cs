using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.ViewModels.Pages;
using Xunit;

namespace Nexstrap.Tests;

public class WelcomeTests
{
    [Theory]
    [InlineData(0, DayPart.Night)]
    [InlineData(4, DayPart.Night)]
    [InlineData(5, DayPart.Morning)]
    [InlineData(11, DayPart.Morning)]
    [InlineData(12, DayPart.Afternoon)]
    [InlineData(17, DayPart.Afternoon)]
    [InlineData(18, DayPart.Evening)]
    [InlineData(23, DayPart.Evening)]
    public void The_day_splits_at_the_right_hours(int hour, DayPart expected) =>
        Assert.Equal(expected, TimeOfDay.Part(hour));

    [Fact]
    public void Each_part_of_the_day_has_its_own_greeting_and_image()
    {
        var parts = Enum.GetValues<DayPart>();

        Assert.Equal(parts.Length, parts.Select(p => WelcomeViewModel.GreetingFor(p)).Distinct().Count());
        Assert.Equal(parts.Length, parts.Select(p => WelcomeViewModel.ImageFile(p)).Distinct().Count());
    }

    [Theory]
    [InlineData(2026, 11, 30, false)]
    [InlineData(2026, 12, 1, true)]
    [InlineData(2026, 12, 25, true)]
    [InlineData(2027, 1, 6, true)]
    [InlineData(2027, 1, 7, false)]
    [InlineData(2027, 7, 1, false)]
    public void Automatic_winter_runs_from_December_to_6_January(int year, int month, int day, bool expected) =>
        Assert.Equal(expected, WinterSeason.IsActive(WinterMode.Automatic, new DateTime(year, month, day)));

    [Fact]
    public void Public_builds_never_show_winter()
    {
        Assert.False(WinterSeason.Available);
        var settings = new WinterSettings { Mode = WinterMode.Always, ChristmasMode = true };

        Assert.Equal((false, Holiday.None), WinterSeason.Resolve(settings, new DateTime(2026, 12, 25), available: false));
    }

    [Fact]
    public void Christmas_mode_wins_over_the_date_and_the_winter_setting()
    {
        var settings = new WinterSettings { Mode = WinterMode.Off, ChristmasMode = true };

        Assert.Equal((true, Holiday.Christmas), WinterSeason.Resolve(settings, new DateTime(2026, 11, 5), available: true));
    }

    [Fact]
    public void Off_also_turns_off_holiday_greetings()
    {
        var settings = new WinterSettings { Mode = WinterMode.Off };

        Assert.Equal((false, Holiday.None), WinterSeason.Resolve(settings, new DateTime(2026, 12, 25), available: true));
    }

    [Fact]
    public void Always_and_off_ignore_the_date()
    {
        Assert.True(WinterSeason.IsActive(WinterMode.Always, new DateTime(2027, 7, 1)));
        Assert.False(WinterSeason.IsActive(WinterMode.Off, new DateTime(2026, 12, 25)));
    }

    [Theory]
    [InlineData(12, 23, Holiday.None)]
    [InlineData(12, 24, Holiday.Christmas)]
    [InlineData(12, 26, Holiday.Christmas)]
    [InlineData(12, 27, Holiday.None)]
    [InlineData(12, 31, Holiday.NewYear)]
    [InlineData(1, 1, Holiday.NewYear)]
    [InlineData(1, 2, Holiday.None)]
    public void Holidays_fall_on_the_right_days(int month, int day, Holiday expected) =>
        Assert.Equal(expected, WinterSeason.HolidayOn(new DateTime(2026, month, day)));

    [Fact]
    public void A_holiday_replaces_the_time_of_day_greeting()
    {
        Assert.NotEqual(WelcomeViewModel.GreetingFor(DayPart.Evening), WelcomeViewModel.GreetingFor(DayPart.Evening, Holiday.Christmas));
        Assert.Equal(WelcomeViewModel.GreetingFor(DayPart.Morning, Holiday.NewYear), WelcomeViewModel.GreetingFor(DayPart.Night, Holiday.NewYear));
    }

    [Fact]
    public void Snowflakes_return_to_the_top_after_leaving_the_window()
    {
        var flake = new Nexstrap.Controls.SnowOverlay.Flake { X = 50, Y = 601, Size = 1, Speed = 40 };

        var moved = Nexstrap.Controls.SnowOverlay.Step(flake, 0.1, 800, 600, new Random(1));

        Assert.True(moved.Y < 0);
        Assert.InRange(moved.X, 0, 800);
    }

    [Fact]
    public void Every_image_is_built_into_the_app()
    {
        var assembly = typeof(WelcomeViewModel).Assembly;
        using var stream = assembly.GetManifestResourceStream("Nexstrap.g.resources");
        using var reader = new System.Resources.ResourceReader(stream!);
        var files = reader.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToList();

        Assert.All(Enum.GetValues<DayPart>(), part =>
        {
            Assert.Contains(WelcomeViewModel.ImageFile(part).ToLowerInvariant(), files);
            Assert.DoesNotContain(WelcomeViewModel.ImageFile(part, winter: true).ToLowerInvariant(), files); // dev only
        });
    }

    [Fact]
    public void Welcome_opens_first_by_default_and_can_be_hidden()
    {
        Assert.Equal("Welcome", SidebarLayout.Visible(new SidebarSettings())[0]);
        Assert.DoesNotContain("Welcome", SidebarLayout.Visible(new SidebarSettings { Hidden = ["Welcome"] }));
    }

    [Fact]
    public void People_with_their_own_order_keep_their_start_page()
    {
        var order = SidebarLayout.Pages.Where(p => p != "Welcome").Reverse().ToList();

        var arranged = SidebarLayout.Arrange(new SidebarSettings { Order = order });

        Assert.Equal(order[0], arranged[0]);
        Assert.Equal("Welcome", arranged[^1]);
    }
}
