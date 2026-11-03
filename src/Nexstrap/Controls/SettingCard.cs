using System.Windows;
using System.Windows.Controls;

namespace Nexstrap.Controls;

/// <summary>
/// A settings row: icon, header and description on the left, the editing control
/// (Content) on the right. Used by every settings page for a consistent layout.
/// </summary>
public class SettingCard : ContentControl
{
    // Template lives in Resources/Styles/Components.xaml (implicit style).

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(null));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingCard), new PropertyMetadata(null));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}
