using System.Windows;
using Microsoft.Win32;

namespace Nexstrap.Services.Dialogs;

public interface IFileDialogService
{
    /// <param name="filter">e.g. "Images|*.png;*.jpg"</param>
    string? OpenFile(string title, string filter);

    string? SaveFile(string title, string filter, string defaultFileName);

    string? PickFolder(string title);
}

/// <summary>Native Windows open/save dialogs, owned by the main window.</summary>
public sealed class FileDialogService : IFileDialogService
{
    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName, OverwritePrompt = true };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }
}
