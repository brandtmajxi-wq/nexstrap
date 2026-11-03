using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;

namespace Nexstrap.Services.Appearance;

public interface IThemeService
{
    /// <summary>Applies everything in <see cref="AppearanceSettings"/> to the running app.</summary>
    void Apply(AppearanceSettings appearance);

    void ApplyTheme(AppTheme theme);

    void ApplyAccent(Color accent);

    void ApplyFont(string familyName);

    /// <summary>Returns false (and clears the background) if the image can't be loaded.</summary>
    bool ApplyBackground(AppearanceSettings appearance);

    /// <summary>Decodes an image (cached), or null if it can't be read.</summary>
    BitmapSource? LoadImage(string path);
}

/// <summary>
/// Low-level resource writer: swaps the theme dictionary and rewrites accent, font and
/// background resources at runtime. All UI references these keys via DynamicResource,
/// so changes show instantly without a restart.
/// </summary>
public sealed class ThemeService(ILogger<ThemeService> logger) : IThemeService
{
    public const string ThemeMarkerKey = "Theme.Name";
    public const string BackgroundBrushKey = "Brush.BackgroundImage";

    /// <summary>Images are decoded no wider than this to keep memory bounded.</summary>
    private const int MaxDecodeWidth = 2560;

    public static readonly Color DefaultAccent = Color.FromRgb(0x7C, 0x6C, 0xFF);

    private static ResourceDictionary Resources => Application.Current.Resources;

    // Avoid re-decoding the same file when only opacity/fit changes.
    private string? _cachedImagePath;
    private BitmapSource? _cachedImage;

    public void Apply(AppearanceSettings appearance)
    {
        ApplyTheme(appearance.Theme);
        ApplyAccent(ColorUtil.TryParseHex(appearance.AccentColor, out var c) ? c : DefaultAccent);
        ApplyFont(appearance.FontFamily);
        ApplyBackground(appearance);
    }

    public void ApplyTheme(AppTheme theme)
    {
        var dictionaries = Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Contains(ThemeMarkerKey));
        if (existing?[ThemeMarkerKey] as string == theme.ToString()) return;

        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Nexstrap;component/Resources/Themes/{theme}.xaml")
        };

        if (existing is null)
            dictionaries.Insert(0, replacement);
        else
            dictionaries[dictionaries.IndexOf(existing)] = replacement;

        UpdateSurfaceTranslucency();
        logger.LogInformation("Theme applied: {Theme}", theme);
    }

    /// <summary>Surfaces that turn slightly see-through while a background image is shown.</summary>
    private static readonly string[] TranslucentSurfaces = ["Brush.Card", "Brush.CardHover", "Brush.Sidebar"];

    private bool _hasBackground;

    /// <summary>
    /// With a background image, overrides surface brushes at app level with translucent copies of
    /// the active theme's colours; without one, removes the overrides so the theme values apply.
    /// </summary>
    private void UpdateSurfaceTranslucency()
    {
        var themeDict = Resources.MergedDictionaries.FirstOrDefault(d => d.Contains(ThemeMarkerKey));
        foreach (var key in TranslucentSurfaces)
        {
            if (_hasBackground && themeDict?[key] is SolidColorBrush source)
                SetBrush(key, ColorUtil.WithAlpha(source.Color, 0xD0));
            else
                Resources.Remove(key);
        }
    }

    public void ApplyAccent(Color accent)
    {
        accent.A = 255;
        SetBrush("Brush.Accent", accent);
        SetBrush("Brush.AccentSoft", ColorUtil.WithAlpha(accent, 0x2E));
        SetBrush("Brush.AccentForeground", ColorUtil.ReadableForeground(accent));
        Resources["Color.Accent"] = accent;
    }

    public void ApplyFont(string familyName)
    {
        var family = string.IsNullOrWhiteSpace(familyName)
            ? new FontFamily($"{AppearanceSettings.DefaultFont}, Segoe UI")
            // Always keep Segoe UI as fallback so missing glyphs never render as boxes.
            : new FontFamily($"{familyName}, Segoe UI");
        Resources["Font.Primary"] = family;
    }

    public bool ApplyBackground(AppearanceSettings appearance)
    {
        var path = appearance.BackgroundImage;
        var image = string.IsNullOrWhiteSpace(path) ? null : LoadImage(path);

        Resources[BackgroundBrushKey] = image is null ? Brushes.Transparent : CreateBrush(image, appearance);
        var hasBackground = image is not null && appearance.BackgroundOpacity > 0;
        if (hasBackground != _hasBackground)
        {
            _hasBackground = hasBackground;
            UpdateSurfaceTranslucency();
        }

        return image is not null || string.IsNullOrWhiteSpace(path);
    }

    internal static ImageBrush CreateBrush(ImageSource image, AppearanceSettings a)
    {
        var brush = new ImageBrush(image)
        {
            Opacity = Math.Clamp(a.BackgroundOpacity, 0, 1),
            Stretch = a.BackgroundFit switch
            {
                BackgroundFit.Fit => Stretch.Uniform,
                BackgroundFit.Stretch => Stretch.Fill,
                BackgroundFit.Center or BackgroundFit.Tile => Stretch.None,
                _ => Stretch.UniformToFill
            },
            AlignmentX = a.BackgroundAnchor switch
            {
                BackgroundAnchor.Left => AlignmentX.Left,
                BackgroundAnchor.Right => AlignmentX.Right,
                _ => AlignmentX.Center
            },
            AlignmentY = a.BackgroundAnchor switch
            {
                BackgroundAnchor.Top => AlignmentY.Top,
                BackgroundAnchor.Bottom => AlignmentY.Bottom,
                _ => AlignmentY.Center
            }
        };

        if (a.BackgroundFit == BackgroundFit.Tile)
        {
            brush.TileMode = TileMode.Tile;
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.Viewport = new Rect(0, 0, image.Width, image.Height);
            brush.Stretch = Stretch.Fill;
        }

        brush.Freeze();
        return brush;
    }

    /// <summary>Decodes fully into memory so the file is never locked.</summary>
    public BitmapSource? LoadImage(string path)
    {
        if (string.Equals(path, _cachedImagePath, StringComparison.OrdinalIgnoreCase) && _cachedImage is not null)
            return _cachedImage;

        try
        {
            if (!File.Exists(path))
            {
                logger.LogWarning("Background image missing: {Path}", path);
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            int pixelWidth;
            using (var probeStream = new MemoryStream(bytes))
                pixelWidth = BitmapDecoder.Create(probeStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0].PixelWidth;

            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (pixelWidth > MaxDecodeWidth) bitmap.DecodePixelWidth = MaxDecodeWidth;
                bitmap.EndInit();
            }
            bitmap.Freeze();

            _cachedImagePath = path;
            _cachedImage = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or FileFormatException)
        {
            logger.LogWarning(ex, "Could not load background image {Path}", path);
            return null;
        }
    }

    private static void SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Resources[key] = brush;
    }
}
