using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexstrap.ViewModels;

/// <summary>One entry in the sidebar.</summary>
public sealed partial class NavItem(string title, string glyph, Type pageType) : ObservableObject
{
    public string Title { get; } = title;

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; } = glyph;

    public Type PageType { get; } = pageType;

    [ObservableProperty]
    private bool _isSelected;
}
