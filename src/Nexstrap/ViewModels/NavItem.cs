using CommunityToolkit.Mvvm.ComponentModel;
using Nexstrap.Core;

namespace Nexstrap.ViewModels;

/// <summary>One entry in the sidebar.</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(string title, string glyph, Type pageType)
    {
        Title = title;
        Glyph = glyph;
        PageType = pageType;
        Loc.Changed += (_, _) => OnPropertyChanged(nameof(Label));
    }

    /// <summary>Fixed English name, also accepted by --page.</summary>
    public string Title { get; }

    /// <summary>The name shown in the current language.</summary>
    public string Label => Loc.GetOr("Nav." + Title.Replace(" ", ""), Title);

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; }

    public Type PageType { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Count shown at the end of the row; 0 hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private int _badge;

    public bool HasBadge => Badge > 0;
}
