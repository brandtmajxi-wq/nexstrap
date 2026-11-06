using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Shell;

namespace Nexstrap.ViewModels.Pages;

public enum LogSource { Nexstrap, Roblox }

/// <summary>Reads Nexstrap's and Roblox's log files inside the app. They're only shown here, never sent anywhere.</summary>
public sealed partial class LogsViewModel(IShellService shell, INavigationService navigation) : PageViewModel
{
    private const int MaxFiles = 15;

    private static string RobloxLogs =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

    private IReadOnlyList<string> _lines = [];

    public override string Title => Loc.Get("Logs.Title");

    public override string Subtitle => Loc.Get("Logs.Subtitle");

    public IReadOnlyList<Choice<LogSource>> SourceChoices { get; } =
        [new(LogSource.Nexstrap, "Nexstrap"), new(LogSource.Roblox, "Roblox")];

    public System.Collections.ObjectModel.ObservableCollection<Choice<string>> Files { get; } = [];

    [ObservableProperty]
    private LogSource _source;

    [ObservableProperty]
    private string? _selectedFile;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private string _status = "";

    partial void OnSourceChanged(LogSource value) => LoadFiles();

    partial void OnSelectedFileChanged(string? value) => Read();

    partial void OnSearchChanged(string value) => Show();

    private string Folder => Source == LogSource.Roblox ? RobloxLogs : AppInfo.Paths.Logs;

    public override void OnNavigatedTo() => LoadFiles();

    [RelayCommand]
    private void Refresh() => LoadFiles();

    [RelayCommand]
    private void OpenFolder() => shell.OpenFolder(Folder);

    [RelayCommand]
    private void Back() => navigation.NavigateTo<AboutViewModel>();

    private void LoadFiles()
    {
        Files.Clear();
        try
        {
            if (Directory.Exists(Folder))
                foreach (var file in new DirectoryInfo(Folder).GetFiles("*.log").OrderByDescending(f => f.LastWriteTime).Take(MaxFiles))
                    Files.Add(new Choice<string>(file.FullName, $"{file.LastWriteTime:g} · {file.Name}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = ex.Message;
        }

        var first = Files.FirstOrDefault()?.Value;
        if (first == SelectedFile) Read();
        else SelectedFile = first;
    }

    private void Read()
    {
        _lines = [];
        if (SelectedFile is { } path)
        {
            try { _lines = LogReader.Tail(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Status = ex.Message;
                Text = "";
                return;
            }
        }
        Show();
    }

    private void Show()
    {
        var shown = LogReader.Filter(_lines, Search);
        Text = string.Join(Environment.NewLine, shown);
        Status = SelectedFile is null ? Loc.Get("Logs.Empty") : Loc.Format("Logs.Showing", shown.Count);
    }
}
