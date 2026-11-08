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
