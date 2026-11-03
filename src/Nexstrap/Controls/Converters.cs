using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nexstrap.Core;
using Nexstrap.Models;

namespace Nexstrap.Controls;

/// <summary>"#RRGGBB" → SolidColorBrush (transparent when invalid).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ColorUtil.TryParseHex(value as string, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>AppTheme → a representative surface colour for small previews. Parameter "window" or "card".</summary>
public sealed class ThemePreviewBrushConverter : IValueConverter
{
    private static readonly Brush DarkWindow = Freeze("#0E0F13"), DarkCard = Freeze("#1C1D24");
    private static readonly Brush LightWindow = Freeze("#ECEDF2"), LightCard = Freeze("#FFFFFF");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var card = string.Equals(parameter as string, "card", StringComparison.OrdinalIgnoreCase);
        return value is AppTheme.Light ? (card ? LightCard : LightWindow) : (card ? DarkCard : DarkWindow);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        ColorUtil.TryParseHex(hex, out var c);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>null/empty → Collapsed; anything else → Visible. Parameter "invert" flips it.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasValue = value is not null && value is not string { Length: 0 };
        if (parameter as string == "invert") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true ↔ false.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>bool → Visibility, inverted.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
