using CommunityToolkit.Mvvm.ComponentModel;
using Nexstrap.Core;

namespace Nexstrap.ViewModels;

/// <summary>Base for every page shown in the main content area.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel()
    {
        Loc.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Subtitle));
        };
    }

    public abstract string Title { get; }

    /// <summary>One-line subtitle shown under the page title.</summary>
    public virtual string Subtitle => string.Empty;

    public virtual void OnNavigatedTo() { }

    public virtual void OnNavigatedFrom() { }
}
