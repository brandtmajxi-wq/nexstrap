using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexstrap.Core;

/// <summary>
/// Keeps hateful and obscene words out of the Welcome greeting. It sees through spacing, accents,
/// repeated letters and number swaps, but no word list is complete, so it's a guard rail and not a guarantee.
/// </summary>
public static class NameFilter
{
    /// <summary>Blocked anywhere in the name, even when the letters are spaced out or joined to other words.</summary>
    private static readonly string[] Anywhere =
    [
        // Mass murderers and hate groups
        "hitler", "himmler", "goebbels", "goering", "heydrich", "eichmann", "mengele", "mussolini", "stalin",
        "polpot", "binladen", "siegheil", "kukluxklan", "holocaust",
        // Slurs
        "nigger", "nigga", "faggot", "tranny", "wetback",
        // Obscene words
        "fuck", "pedophil", "paedophil", "motherfuck", "retard", "hurensohn", "wichser", "arschloch", "maricon", "caralho",
        "хуй", "хуе", "пизд", "ебат", "ебан", "блять", "пидор", "гитлер"
    ];

    /// <summary>Only blocked as a whole word, because these letters also appear inside ordinary names, such as Nazim.</summary>
    private static readonly string[] WholeWord =
    [
        "nazi", "nazis", "kkk", "heil", "fuhrer", "fuehrer",
        "fag", "fags", "chink", "gook", "spic", "paki", "neger", "kike",
        "shit", "bitch", "cunt", "slut", "whore", "rape", "rapist", "porn", "penis", "vagina", "fotze", "schlampe",
        "puta", "salope", "encule", "viado", "сука"
    ];

    private static readonly Regex AnywherePattern = Build(Anywhere);
    private static readonly Regex WholeWordPattern = new("^(?:" + string.Join('|', WholeWord.Select(Stretch)) + ")$", RegexOptions.CultureInvariant);

    public static bool IsAllowed(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;

        // A 1 could stand for either letter, so both readings are checked.
        return Readings(name).All(text =>
        {
            var words = Words(text);
            return !AnywherePattern.IsMatch(string.Concat(words)) && !words.Any(WholeWordPattern.IsMatch);
        });
    }

    private static Regex Build(IEnumerable<string> terms) =>
        new(string.Join('|', terms.Select(Stretch)), RegexOptions.CultureInvariant);

    /// <summary>"nigger" becomes n+i+g{2,}e+r+, so stretched spellings match without "gg" turning into a plain "g".</summary>
    private static string Stretch(string term)
    {
        var pattern = new StringBuilder();
        for (var i = 0; i < term.Length;)
        {
            var end = i;
            while (end < term.Length && term[end] == term[i]) end++;
            pattern.Append(Regex.Escape(term[i].ToString())).Append(end - i == 1 ? "+" : $"{{{end - i},}}");
            i = end;
        }
        return pattern.ToString();
    }

    private static IEnumerable<string> Readings(string name)
    {
        var plain = Plain(name);
        yield return Swap(plain, one: 'i');
        yield return Swap(plain, one: 'l');
        yield return Swap(Lookalikes(plain), one: 'i');
    }

    /// <summary>Lower case without accents, so ü reads as u.</summary>
    private static string Plain(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) text.Append(c);
        return text.ToString();
    }

    private static string Swap(string text, char one) => new(text.Select(c => c switch
    {
        '0' => 'o',
        '1' or '!' or '|' => one,
        '3' => 'e',
        '4' or '@' => 'a',
        '5' or '$' => 's',
        '7' => 't',
        '8' => 'b',
        _ => c
    }).ToArray());

    /// <summary>Cyrillic letters that look Latin, which can disguise a word like Hitler.</summary>
    private static string Lookalikes(string text) => new(text.Select(c => c switch
    {
        'а' => 'a', 'е' => 'e', 'о' => 'o', 'р' => 'p', 'с' => 'c', 'х' => 'x', 'у' => 'y', 'і' => 'i', 'т' => 't', 'н' => 'h', 'к' => 'k',
        _ => c
    }).ToArray());

    /// <summary>Single letters in a row count as one word, so "H i t l e r" is read as "hitler".</summary>
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var spaced = new StringBuilder();
        foreach (var word in Regex.Split(text, @"[^\p{L}]+").Where(w => w.Length > 0))
        {
            if (word.Length == 1) { spaced.Append(word); continue; }
            if (spaced.Length > 0) { words.Add(spaced.ToString()); spaced.Clear(); }
            words.Add(word);
        }
        if (spaced.Length > 0) words.Add(spaced.ToString());
        return words;
    }
}
