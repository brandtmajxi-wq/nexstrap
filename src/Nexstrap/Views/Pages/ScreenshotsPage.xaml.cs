using System.Windows.Controls;
using System.Windows.Input;
using Nexstrap.ViewModels.Pages;

namespace Nexstrap.Views.Pages;

public partial class ScreenshotsPage : UserControl
{
    public ScreenshotsPage() => InitializeComponent();

    /// <summary>While picking the clip key, the next key pressed (with Ctrl/Alt/Shift) becomes the new key.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ScreenshotsViewModel { PickingHotkey: true } vm) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        vm.TrySetHotkey(Keyboard.Modifiers, key);
        e.Handled = true; // so Space/Enter don't click the button again
    }

    private void OnHotkeyLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is ScreenshotsViewModel vm) vm.PickingHotkey = false;
    }
}
