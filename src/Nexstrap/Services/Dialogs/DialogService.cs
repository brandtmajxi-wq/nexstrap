using System.Windows;
using Nexstrap.Core;
using Nexstrap.Views.Dialogs;

namespace Nexstrap.Services.Dialogs;

public enum DialogKind
{
    Info,
    Warning,
    Error,
    Question
}

public interface IDialogService
{
    void Show(string title, string message, DialogKind kind = DialogKind.Info, string? details = null);

    bool Confirm(string title, string message, string confirmText = "Continue", string cancelText = "Cancel");

    /// <summary>Asks for one line of text. Returns null if it was skipped. <paramref name="validate"/> returns a problem to show, or null.</summary>
    string? Ask(string title, string message, string confirmText, string cancelText, int maxLength, Func<string, string?>? validate = null);
}

/// <summary>Shows Nexstrap-styled modal dialogs instead of the stock MessageBox.</summary>
public sealed class DialogService : IDialogService
{
    public void Show(string title, string message, DialogKind kind = DialogKind.Info, string? details = null) =>
        Run(() => MessageDialog.ShowModal(Owner(), title, message, kind, details, Loc.Get("Common.OK"), null));

    public bool Confirm(string title, string message, string confirmText = "Continue", string cancelText = "Cancel") =>
        Run(() => MessageDialog.ShowModal(Owner(), title, message, DialogKind.Question, null,
            Loc.GetOr("Common." + confirmText, confirmText), Loc.GetOr("Common." + cancelText, cancelText)));

    public string? Ask(string title, string message, string confirmText, string cancelText, int maxLength, Func<string, string?>? validate = null)
    {
        string? answer = null;
        Run(() => answer = MessageDialog.AskText(Owner(), title, message, confirmText, cancelText, maxLength, validate));
        return answer;
    }

    private static Window? Owner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
        ?? (Application.Current?.MainWindow is { IsVisible: true } main ? main : null);

    private static bool Run(Func<bool> show)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return false;
        return dispatcher.CheckAccess() ? show() : dispatcher.Invoke(show);
    }

    private static void Run(Action show) => Run(() => { show(); return true; });
}
