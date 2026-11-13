using System.Globalization;
using Nexstrap.Core;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class HistoryExportTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void German_excel_gets_semicolons_and_decimal_commas()
    {
        var start = new DateTime(2026, 11, 7, 18, 30, 0, DateTimeKind.Local).ToUniversalTime();
        var csv = HistoryExport.Sessions([new PlaySession { PlaceId = 42, StartedUtc = start, Seconds = 1530 }],
            new Dictionary<long, string> { [42] = "Grüne Insel" }, German);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(5, lines[0].Split(';').Length);
        Assert.Equal("2026-11-07;18:30;Grüne Insel;42;25,5", lines[1]);
    }

    [Fact]
    public void English_excel_gets_commas_and_names_with_commas_are_quoted()
    {
        var csv = HistoryExport.Games([new GamePlayStats { PlaceId = 7, Name = "Work at a Pizza Place, Again", Seconds = 5400, Sessions = 3 }], English);
        var row = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.StartsWith("\"Work at a Pizza Place, Again\",7,1.50,3,", row);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "'=HYPERLINK(\"x\")")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-5", "-5")]
    [InlineData("Say \"hi\"", "Say \"hi\"")]
    public void Formulas_are_defused(string value, string expectedBeforeQuoting)
    {
        var field = HistoryExport.Field(value, ";");
        var unquoted = field.StartsWith('"') ? field[1..^1].Replace("\"\"", "\"") : field;
        Assert.Equal(expectedBeforeQuoting, unquoted);
    }

    [Fact]
    public void A_separator_that_is_also_the_decimal_mark_falls_back_to_a_semicolon()
    {
        var odd = (CultureInfo)English.Clone();
        odd.TextInfo.ListSeparator = ".";
        Assert.Equal(";", HistoryExport.Separator(odd));
        Assert.Equal(",", HistoryExport.Separator(English));
    }
}
