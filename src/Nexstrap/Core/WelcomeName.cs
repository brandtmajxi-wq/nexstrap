using System.Globalization;
using System.Text;

namespace Nexstrap.Core;

/// <summary>Tidies the name people type for the Welcome page, and adds it to a greeting.</summary>
public static class WelcomeName
{
    public const int MaxLength = 32;

    /// <summary>Removes control characters and extra spaces, and limits the length. Empty becomes null.</summary>
    public static string? Clean(string? text)
    {
        if (text is null) return null;

        var parts = new string(text.Where(c => !char.IsControl(c)).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var tidy = string.Join(' ', parts);

        // Cut by whole characters, so an emoji is never split in half.
        var result = new StringBuilder();
        var letters = StringInfo.GetTextElementEnumerator(tidy);
        while (letters.MoveNext() && result.Length + letters.GetTextElement().Length <= MaxLength)
            result.Append(letters.GetTextElement());

        var cleaned = result.ToString().TrimEnd();
        return cleaned.Length == 0 ? null : cleaned;
    }

    public static string Greet(string greeting, string? name) =>
        Clean(name) is { } known && NameFilter.IsAllowed(known) ? $"{greeting}, {known}" : greeting;
}
