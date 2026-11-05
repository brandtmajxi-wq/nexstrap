using Nexstrap.Core;
using Xunit;

namespace Nexstrap.Tests;

public class NameFilterTests
{
    [Theory]
    [InlineData("Hitler")]
    [InlineData("HITLER")]
    [InlineData("Adolf Hitler")]
    [InlineData("h1tler")]
    [InlineData("hit1er")]
    [InlineData("H i t l e r")]
    [InlineData("H.i.t.l.e.r")]
    [InlineData("Hiiitlerrr")]
    [InlineData("Hítlér")]
    [InlineData("xXHitlerXx")]
    [InlineData("Нitlеr")] // Cyrillic H and e
    [InlineData("Гитлер")]
    [InlineData("nazi")]
    [InlineData("N4Z1")]
    [InlineData("Sieg Heil")]
    [InlineData("Führer")]
    [InlineData("niggerrr")]
    [InlineData("fuck you")]
    [InlineData("Sh1t")]
    public void Blocks_hateful_and_obscene_names(string name) => Assert.False(NameFilter.IsAllowed(name));

    [Theory]
    [InlineData("Liam")]
    [InlineData("Brandt")]
    [InlineData("Nazim")]
    [InlineData("Nazir")]
    [InlineData("Mitchell")]
    [InlineData("Adolf")]
    [InlineData("Niger")]
    [InlineData("Ashitaka")]
    [InlineData("Cassandra")]
    [InlineData("Scunthorpe")]
    [InlineData("Ana María")]
    [InlineData("Саша")]
    [InlineData("Дмитрий")]
    [InlineData("🎄 Liam")]
    [InlineData("")]
    public void Allows_ordinary_names(string name) => Assert.True(NameFilter.IsAllowed(name));

    [Fact]
    public void A_blocked_name_already_saved_is_left_out_of_the_greeting() =>
        Assert.Equal("Good evening", WelcomeName.Greet("Good evening", "Hitler"));
}
