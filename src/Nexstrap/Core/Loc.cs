using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace Nexstrap.Core;

public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Translated text from Resources\Languages\*.json. XAML uses {DynamicResource L.Some.Key} and code uses
/// Loc.Get("Some.Key"). A key missing from a language falls back to English.
/// </summary>
public static class Loc
{
    public const string DefaultCode = "en";

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("fr", "Français"),
        new("pt", "Português (Brasil)"),
        new("ru", "Русский")
    ];

    private static readonly Dictionary<string, string> English = Load(DefaultCode);
    private static Dictionary<string, string> _current = English;

    public static string Code { get; private set; } = DefaultCode;

    /// <summary>Raised after the language changes.</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyCollection<string> Keys => English.Keys;

    public static string Get(string key) => GetOr(key, key);

    public static string GetOr(string key, string fallback) =>
        _current.TryGetValue(key, out var text) || English.TryGetValue(key, out text) ? text : fallback;

    /// <summary>The Windows display language if Nexstrap has it, otherwise English.</summary>
    public static string Detect(CultureInfo culture) =>
        Languages.FirstOrDefault(l => l.Code == culture.TwoLetterISOLanguageName)?.Code ?? DefaultCode;

    public static void Apply(string? code)
    {
        code = Languages.FirstOrDefault(l => l.Code == code)?.Code ?? Detect(CultureInfo.CurrentUICulture);
        Code = code;
        _current = code == DefaultCode ? English : Load(code);

        if (Application.Current is { } app)
            foreach (var key in English.Keys)
                app.Resources["L." + key] = Get(key);

        Changed?.Invoke(null, EventArgs.Empty);
    }

    internal static Dictionary<string, string> Load(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Nexstrap.Languages.{code}.json")
            ?? throw new FileNotFoundException($"Language {code} is missing from the app.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
