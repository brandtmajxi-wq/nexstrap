using Nexstrap.Services.Notifications;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class SessionSummaryTests
{
    private static readonly DateTime Opened = new(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc);

    private static readonly GamePlayStats[] Games =
    [
        new() { PlaceId = 1, Name = "Adopt Me!" },
        new() { PlaceId = 2, Name = "Brookhaven" },
        new() { PlaceId = 3, Name = "Doors" }
    ];

    private static PlaySession Game(long place, int minutesAfterOpen, int minutes) =>
        new() { PlaceId = place, StartedUtc = Opened.AddMinutes(minutesAfterOpen), Seconds = minutes * 60 };

    [Fact]
    public void Under_two_minutes_of_play_shows_nothing()
    {
        Assert.Null(SessionSummary.Build([Game(1, 1, 1)], Games, Opened, 600));
        Assert.Null(SessionSummary.Build([], Games, Opened, 600));
    }

    [Fact]
    public void Games_from_before_roblox_opened_dont_count()
    {
        var note = SessionSummary.Build([Game(2, -90, 60), Game(1, 2, 30)], Games, Opened, 90 * 60);
        Assert.NotNull(note);
        Assert.Contains("30", note.Value.Title);
        Assert.StartsWith("Adopt Me!", note.Value.Text);
        Assert.DoesNotContain("Brookhaven", note.Value.Text);
    }

    [Fact]
    public void Several_games_name_the_most_played_and_count_the_rest()
    {
        var note = SessionSummary.Build([Game(1, 1, 5), Game(3, 7, 40), Game(2, 50, 10), Game(3, 61, 5)], Games, Opened, 3 * 3600);
        Assert.NotNull(note);
        Assert.StartsWith("Doors", note.Value.Text);
        Assert.Contains("2", note.Value.Text);
        Assert.Contains("1 h", note.Value.Title);
    }

    [Fact]
    public void Today_is_never_shown_as_less_than_this_session()
    {
        var note = SessionSummary.Build([Game(1, 1, 50)], Games, Opened, 0);
        Assert.NotNull(note);
        Assert.Equal(2, note.Value.Text.Split("50").Length - 1 + note.Value.Title.Split("50").Length - 1);
    }
}
