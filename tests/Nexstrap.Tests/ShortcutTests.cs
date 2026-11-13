using System.Windows.Input;
using Nexstrap.Core;
using Xunit;

namespace Nexstrap.Tests;

public class ShortcutTests
{
    [Theory]
    [InlineData(Key.D1, 1)]
    [InlineData(Key.D9, 9)]
    [InlineData(Key.NumPad4, 4)]
    public void Ctrl_and_a_number_opens_that_page(Key key, int page) =>
        Assert.Equal((ShortcutAction.Page, page), Shortcuts.Match(key, ModifierKeys.Control));

    [Theory]
    [InlineData(Key.P, ShortcutAction.Play)]
    [InlineData(Key.J, ShortcutAction.Rejoin)]
    [InlineData(Key.K, ShortcutAction.Search)]
    [InlineData(Key.N, ShortcutAction.Notifications)]
    public void Ctrl_letters(Key key, ShortcutAction action) =>
        Assert.Equal(action, Shortcuts.Match(key, ModifierKeys.Control).Action);

    [Theory]
    [InlineData(Key.P, ModifierKeys.None)]
    [InlineData(Key.D0, ModifierKeys.Control)]
    [InlineData(Key.N, ModifierKeys.Control | ModifierKeys.Alt)]   // the global hotkey, handled elsewhere
    [InlineData(Key.C, ModifierKeys.Control)]                      // copy keeps working in text boxes
    [InlineData(Key.V, ModifierKeys.Control)]
    [InlineData(Key.A, ModifierKeys.Control)]
    public void Everything_else_is_left_alone(Key key, ModifierKeys modifiers) =>
        Assert.Equal(ShortcutAction.None, Shortcuts.Match(key, modifiers).Action);

    [Fact]
    public void The_list_names_every_shortcut() => Assert.Equal(5, Shortcuts.List().Count);
}
