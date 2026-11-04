using System.IO;
using System.Text.RegularExpressions;
using Nexstrap.Core;
using Xunit;

namespace Nexstrap.Tests;

public class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{\d+(:[^}]*)?\}");

    public static TheoryData<string> Translations()
    {
        var data = new TheoryData<string>();
        foreach (var language in Loc.Languages.Where(l => l.Code != Loc.DefaultCode)) data.Add(language.Code);
        return data;
    }

    private static IEnumerable<string> Holes(string text) =>
        Placeholder.Matches(text).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal);

    private static string SourceFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nexstrap.slnx"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Nexstrap.slnx not found."), "src", "Nexstrap");
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(SourceFolder(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_language_has_exactly_the_english_keys(string code)
    {
        var english = Loc.Load(Loc.DefaultCode);
        var other = Loc.Load(code);

        Assert.Empty(english.Keys.Except(other.Keys));
        Assert.Empty(other.Keys.Except(english.Keys));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translations_keep_placeholders_and_outer_spaces(string code)
    {
        var other = Loc.Load(code);
        foreach (var (key, text) in Loc.Load(Loc.DefaultCode))
        {
            Assert.True(Holes(text).SequenceEqual(Holes(other[key])), $"{code} {key}: placeholders differ");
            Assert.True(text.StartsWith(' ') == other[key].StartsWith(' ') && text.EndsWith(' ') == other[key].EndsWith(' '),
                $"{code} {key}: outer spaces differ");
            Assert.False(string.IsNullOrWhiteSpace(other[key]), $"{code} {key}: empty");
        }
    }

    [Fact]
    public void Every_key_used_in_xaml_exists()
    {
        var keys = Loc.Load(Loc.DefaultCode);
        var missing = SourceFiles("*.xaml")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"DynamicResource L\.([^}\s]+)\}").Select(m => m.Groups[1].Value))
            .Concat(SourceFiles("*.xaml").SelectMany(f => Regex.Matches(File.ReadAllText(f), @"ConverterParameter='?([A-Za-z.|]+)'?\}")
                .SelectMany(m => m.Groups[1].Value.Split('|'))).Where(k => k.Contains('.')))
            .Distinct().Where(k => !keys.ContainsKey(k)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_key_used_in_code_exists()
    {
        var keys = Loc.Load(Loc.DefaultCode);
        var missing = SourceFiles("*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"Loc\.(?:Get|Format)\(""([^""]+)""").Select(m => m.Groups[1].Value))
            .Distinct().Where(k => !keys.ContainsKey(k)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Pages_have_no_hard_coded_visible_text()
    {
        var text = new Regex(@"(?<![\w.:])(?:Text|Content|Header|Description|ToolTip|ui:Ui\.Placeholder)=""(?<v>[^""{]*[A-Za-z]{2}[^""{]*)""");
        var allowed = new HashSet<string> { "Discord", "Roblox", "Rich Presence", "Aa Bb Cc 123", "Inside Nexstrap", "Browsing Roblox" };
        var pages = Path.Combine(SourceFolder(), "Views", "Pages");

        var found = Directory.EnumerateFiles(pages, "*.xaml")
            .SelectMany(f => text.Matches(File.ReadAllText(f)).Select(m => m.Groups["v"].Value))
            .Where(v => !v.StartsWith("&#x", StringComparison.Ordinal) && !v.StartsWith('#') && !allowed.Contains(v))
            .ToList();

        Assert.Empty(found);
    }
}
