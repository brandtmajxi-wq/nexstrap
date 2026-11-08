using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexstrap.ViewModels.Pages;

namespace Nexstrap.Views.Pages;

public partial class MoscowPage : UserControl
{
    public MoscowPage() => InitializeComponent();

    /// <summary>A PasswordBox can't be bound, so the password goes straight to the page and is cleared.</summary>
    private void OnUnlock(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MoscowViewModel page) return;
        page.TryUnlock(Password.Password);
        Password.Clear();
        Password.Focus();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnUnlock(sender, e);
        e.Handled = true;
    }
}
