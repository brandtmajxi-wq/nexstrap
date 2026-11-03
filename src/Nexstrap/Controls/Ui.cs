using System.Windows;
using System.Windows.Media;

namespace Nexstrap.Controls;

/// <summary>
/// Attached properties consumed by the shared control templates, so one template
/// serves many styles (e.g. buttons with/without icons, different radii).
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Ui),
        new FrameworkPropertyMetadata(new CornerRadius(8), FrameworkPropertyMetadataOptions.Inherits));

    public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject o, CornerRadius v) => o.SetValue(CornerRadiusProperty, v);

    /// <summary>Overlay painted over the control while hovered (faded in/out).</summary>
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.RegisterAttached(
        "HoverBrush", typeof(Brush), typeof(Ui), new PropertyMetadata(null));

    public static Brush? GetHoverBrush(DependencyObject o) => (Brush?)o.GetValue(HoverBrushProperty);
    public static void SetHoverBrush(DependencyObject o, Brush? v) => o.SetValue(HoverBrushProperty, v);

    /// <summary>Segoe Fluent Icons glyph shown before the content.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string? GetIcon(DependencyObject o) => (string?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string? v) => o.SetValue(IconProperty, v);

    /// <summary>Hint text shown in an empty TextBox.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);
}
