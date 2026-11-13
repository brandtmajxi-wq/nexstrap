using Nexstrap.ViewModels.Pages;
using Xunit;

namespace Nexstrap.Tests;

public class SurpriseTests
{
    [Fact]
    public void Never_the_same_game_twice_in_a_row_when_there_is_a_choice()
    {
        var random = new Random(7);
        long? last = null;
        for (var i = 0; i < 200; i++)
        {
            var pick = GamesViewModel.Pick([1, 2, 3], last, random);
            Assert.NotEqual(last, pick);
            last = pick;
        }
    }

    [Fact]
    public void Every_game_can_come_up()
    {
        var random = new Random(1);
        var seen = Enumerable.Range(0, 300).Select(_ => GamesViewModel.Pick([10, 20, 30, 40], null, random)).ToHashSet();
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public void A_single_game_is_always_picked() =>
        Assert.Equal(5, GamesViewModel.Pick([5], 5, new Random()));

    [Theory]
    [InlineData("  code: SPOOKY  ", "code: SPOOKY")]
    [InlineData("line one\r\nline two", "line one line two")]
    [InlineData("\t\n ", "")]
    public void Notes_are_tidied(string typed, string saved) => Assert.Equal(saved, GamesViewModel.Tidy(typed));

    [Fact]
    public void Notes_are_cut_at_200_characters() =>
        Assert.Equal(GamesViewModel.NoteLength, GamesViewModel.Tidy(new string('a', 500)).Length);

    [Fact]
    public void Notes_survive_saving_and_loading_settings()
    {
        var settings = new Nexstrap.Models.AppSettings();
        settings.Launcher.GameNotes[920587237] = "Trade with Max on Friday";
        var json = System.Text.Json.JsonSerializer.Serialize(settings, Nexstrap.Services.Configuration.JsonFileStore<Nexstrap.Models.AppSettings>.SerializerOptions);
        var back = System.Text.Json.JsonSerializer.Deserialize<Nexstrap.Models.AppSettings>(json, Nexstrap.Services.Configuration.JsonFileStore<Nexstrap.Models.AppSettings>.SerializerOptions)!;
        Assert.Equal("Trade with Max on Friday", back.Launcher.GameNotes[920587237]);
    }
}
