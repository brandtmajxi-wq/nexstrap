using Nexstrap.Core;
using Xunit;

namespace Nexstrap.Tests;

public class SecretRoomTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("password")]
    [InlineData("PASSWORD_HASH_PLACEHOLDER")]
    public void Wrong_or_empty_passwords_dont_open_it(string? password) => Assert.False(SecretRoom.Check(password));

    [Fact]
    public void Only_a_fingerprint_is_stored()
    {
        Assert.Equal(64, SecretRoom.PasswordHash.Length);
        Assert.True(SecretRoom.PasswordHash.All(Uri.IsHexDigit));
    }

    [Fact]
    public void Spaces_around_the_password_dont_matter() =>
        Assert.Equal(SecretRoom.Fingerprint("abc"), SecretRoom.Fingerprint("  abc "));

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Moscow_welcome_needs_the_room_unlocked_and_the_switch_on(bool unlocked, bool scene, bool expected)
    {
        var settings = new Nexstrap.Models.AppSettings { SecretRoomUnlocked = unlocked };
        settings.Welcome.MoscowScene = scene;
        Assert.Equal(expected, Nexstrap.ViewModels.Pages.WelcomeViewModel.ShowsMoscow(settings));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Berlin_needs_the_dev_build_and_the_switch_on(bool available, bool scene, bool expected)
    {
        var settings = new Nexstrap.Models.AppSettings();
        settings.Welcome.BerlinScene = scene;
        Assert.Equal(expected, Nexstrap.ViewModels.Pages.WelcomeViewModel.ShowsBerlin(settings, available));
    }

    [Theory]
    [InlineData(DayPart.Morning, null, "Guten Morgen!")]
    [InlineData(DayPart.Afternoon, "Liam", "Guten Tag, Liam!")]
    [InlineData(DayPart.Night, "", "Gute Nacht!")]
    public void Greets_in_german(DayPart part, string? name, string expected) =>
        Assert.Equal(expected, Nexstrap.ViewModels.Pages.WelcomeViewModel.GermanGreeting(part, name));

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(5)]
    public void No_snow_in_summer(int month)
    {
        for (var roll = 0.0; roll < 1; roll += 0.001)
            Assert.NotEqual(Weather.Snow, MoscowWeather.Pick(month, roll));
    }

    [Fact]
    public void Winter_has_no_thunderstorms_but_does_snow()
    {
        var picks = Enumerable.Range(0, 1000).Select(i => MoscowWeather.Pick(1, i / 1000.0)).ToHashSet();
        Assert.DoesNotContain(Weather.Storm, picks);
        Assert.DoesNotContain(Weather.Rain, picks);
        Assert.Contains(Weather.Snow, picks);
    }

    [Fact]
    public void Every_month_can_be_any_of_several_weathers()
    {
        for (var month = 1; month <= 12; month++)
        {
            Assert.Equal(MoscowWeather.Scenes.Length, MoscowWeather.Weights(month).Length);
            Assert.True(Enumerable.Range(0, 1000).Select(i => MoscowWeather.Pick(month, i / 1000.0)).Distinct().Count() >= 4);
        }
    }

    [Fact]
    public void A_fixed_weather_or_off_is_used_as_it_is()
    {
        Assert.Equal(Weather.Fog, MoscowWeather.Resolve(Weather.Fog, DateTime.Now));
        Assert.Equal(Weather.Off, MoscowWeather.Resolve(Weather.Off, DateTime.Now));
        var random = MoscowWeather.Resolve(Weather.Random, DateTime.Now);
        Assert.Contains(random, MoscowWeather.Scenes);
        Assert.Equal(random, MoscowWeather.Resolve(Weather.Random, DateTime.Now));
    }

    [Fact]
    public void Weather_only_shows_on_moscow_and_not_when_off()
    {
        Assert.Null(Nexstrap.ViewModels.Pages.WelcomeViewModel.MoscowSky(false, Weather.Rain, DayPart.Night));
        Assert.Null(Nexstrap.ViewModels.Pages.WelcomeViewModel.MoscowSky(true, Weather.Off, DayPart.Night));
        Assert.Equal(new SkyScene(Weather.Rain, DayPart.Night), Nexstrap.ViewModels.Pages.WelcomeViewModel.MoscowSky(true, Weather.Rain, DayPart.Night));
    }

    [Fact]
    public void Lightning_strikes_flickers_and_fades()
    {
        Assert.Equal(1, Nexstrap.Controls.WeatherOverlay.Flash(0.02));
        Assert.True(Nexstrap.Controls.WeatherOverlay.Flash(0.1) < Nexstrap.Controls.WeatherOverlay.Flash(0.2));
        Assert.True(Nexstrap.Controls.WeatherOverlay.Flash(1.5) < 0.01);
    }

    [Fact]
    public void Every_weather_has_a_russian_name()
    {
        foreach (var weather in MoscowWeather.Scenes)
            Assert.False(string.IsNullOrEmpty(MoscowWeather.RussianName(weather, DayPart.Afternoon)));
        Assert.Equal("Ясная ночь", MoscowWeather.RussianName(Weather.Clear, DayPart.Night));
    }

    [Fact]
    public void Moscow_is_three_hours_ahead_of_utc() =>
        Assert.Equal(new DateTime(2026, 11, 8, 15, 0, 0), SecretRoom.MoscowTime(new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc)));

    [Theory]
    [InlineData(DayPart.Morning, null, "Доброе утро!")]
    [InlineData(DayPart.Evening, "Max", "Добрый вечер, Max!")]
    [InlineData(DayPart.Night, " ", "Доброй ночи!")]
    public void Greets_in_russian(DayPart part, string? name, string expected) =>
        Assert.Equal(expected, SecretRoom.Greeting(part, name));
}
