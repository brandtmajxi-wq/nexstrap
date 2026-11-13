using Nexstrap.Services.Notifications;
using Xunit;

namespace Nexstrap.Tests;

public class DailyLimitTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    [Theory]
    [InlineData(0, 999999, null, false)]          // off
    [InlineData(120, 119 * 60, null, false)]      // not there yet
    [InlineData(120, 120 * 60, null, true)]       // reached
    [InlineData(120, 300 * 60, "2026-11-09", true)] // shown yesterday, so again today
    [InlineData(120, 300 * 60, "2026-11-10", false)] // already shown today
    public void Shows_once_a_day_after_the_limit(int limit, double seconds, string? shownOn, bool expected) =>
        Assert.Equal(expected, DailyLimit.Due(seconds, limit, shownOn, Today));

    [Fact]
    public void Days_are_stored_the_same_way_in_every_language() =>
        Assert.Equal("2026-11-10", DailyLimit.Day(Today));

    [Fact]
    public void Choices_start_with_off() => Assert.Equal(0, DailyLimit.Choices[0]);
}
