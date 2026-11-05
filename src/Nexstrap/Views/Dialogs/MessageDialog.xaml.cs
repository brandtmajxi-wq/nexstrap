using System.Windows;
using System.Windows.Input;
using Nexstrap.Services.Dialogs;

namespace Nexstrap.Views.Dialogs;

public partial class MessageDialog : Window
{
    private MessageDialog()
    {
        InitializeComponent();
    }

    public static bool ShowModal(Window? owner, string title, string message, DialogKind kind,
        string? details, string confirmText, string? cancelText)
    {
        var dialog = new MessageDialog();
        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;

        if (cancelText is null)
            dialog.CancelButton.Visibility = Visibility.Collapsed;
        else
            dialog.CancelButton.Content = cancelText;

        if (!string.IsNullOrWhiteSpace(details))
        {
            dialog.DetailsExpander.Visibility = Visibility.Visible;
            dialog.DetailsText.Text = details;
        }

        (string glyph, string brush, string soft) = kind switch
        {
            DialogKind.Warning => ("", "Brush.Warning", "Brush.WarningSoft"),
            DialogKind.Error => ("", "Brush.Danger", "Brush.DangerSoft"),
            DialogKind.Question => ("", "Brush.Accent", "Brush.AccentSoft"),
            _ => ("", "Brush.Accent", "Brush.AccentSoft")
        };
        dialog.IconGlyph.Text = glyph;
        dialog.IconGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, brush);
        dialog.IconBadge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, soft);

        return dialog.ShowDialog() == true;
    }

    /// <summary>Asks for one line of text. Returns null if it was skipped.</summary>
    public static string? AskText(Window? owner, string title, string message, string confirmText, string cancelText, int maxLength)
    {
        var dialog = new MessageDialog();
        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;
        dialog.CancelButton.Content = cancelText;
        dialog.IconGlyph.Text = char.ConvertFromUtf32(0xE77B);
        dialog.AnswerBox.MaxLength = maxLength;
        dialog.AnswerBox.Visibility = Visibility.Visible;
        dialog.Loaded += (_, _) => dialog.AnswerBox.Focus();

        return dialog.ShowDialog() == true ? dialog.AnswerBox.Text : null;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
