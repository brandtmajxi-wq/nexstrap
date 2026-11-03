using System.Windows;
using System.Windows.Controls;
using Nexstrap.ViewModels.Pages;

namespace Nexstrap.Views.Pages;

public partial class FastFlagsPage : UserControl
{
    public FastFlagsPage() => InitializeComponent();

    /// <summary>Leaving a value box saves it, same as pressing Enter.</summary>
    private void OnValueLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FlagRow row } && row.CommitValueCommand.CanExecute(null))
            row.CommitValueCommand.Execute(null);
    }
}
