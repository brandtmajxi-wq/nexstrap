using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace Nexstrap.Services.Appearance;

/// <summary>An installed font family. <see cref="IsSymbol"/> fonts (Wingdings…) can't render normal text.</summary>
public sealed record FontOption(string Name, FontFamily Family, bool IsSymbol);

public interface IFontCatalog
{
    /// <summary>All installed text fonts, sorted by name. Cached after the first call.</summary>
    Task<IReadOnlyList<FontOption>> GetFontsAsync();
}

public sealed class FontCatalog(ILogger<FontCatalog> logger) : IFontCatalog
{
    private Task<IReadOnlyList<FontOption>>? _cache;

    public Task<IReadOnlyList<FontOption>> GetFontsAsync() => _cache ??= Task.Run(Load);

    private IReadOnlyList<FontOption> Load()
    {
        var language = XmlLanguage.GetLanguage("en-us");
        var fonts = new List<FontOption>();

        foreach (var family in Fonts.SystemFontFamilies)
        {
            try
            {
                var name = family.FamilyNames.TryGetValue(language, out var n) ? n : family.Source;
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@')) continue; // vertical CJK aliases

                var isSymbol = family.GetTypefaces().FirstOrDefault() is { } face
                               && face.TryGetGlyphTypeface(out var glyphs) && glyphs.Symbol;
                fonts.Add(new FontOption(name, family, isSymbol));
            }
            catch (Exception ex)
            {
                // Broken font files exist in the wild; skip them rather than fail the list.
                logger.LogDebug(ex, "Skipping font {Font}", family.Source);
            }
        }

        var result = fonts
            .Where(f => !f.IsSymbol)
            .DistinctBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        logger.LogInformation("Loaded {Count} text fonts ({Symbols} symbol fonts hidden)", result.Count, fonts.Count(f => f.IsSymbol));
        return result;
    }
}
