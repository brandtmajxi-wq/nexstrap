using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexstrap.Core;

namespace Nexstrap.ViewModels;

/// <summary>Base for every page shown in the main content area.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel()
    {
        // An empty name refreshes every binding, so calculated text follows the new language.
        Loc.Changed += (_, _) => OnPropertyChanged(string.Empty);
    }

    public abstract string Title { get; }

    /// <summary>One-line subtitle shown under the page title.</summary>
    public virtual string Subtitle => string.Empty;

    /// <summary>A full-window picture for this page, shown over any custom background.</summary>
    public virtual ImageSource? Backdrop => null;

    /// <summary>False for pages that draw their own heading.</summary>
    public virtual bool HasHeader => true;

    public virtual void OnNavigatedTo() { }

    public virtual void OnNavigatedFrom() { }
}
