using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Services.Logging;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Shell;

namespace Nexstrap.ViewModels.Pages;

public sealed record ThirdPartyComponent(string Name, string License, string Url);

public sealed partial class AboutViewModel(IShellService shell, FileLoggerProvider logs, INavigationService navigation) : PageViewModel
{
    public override string Title => Loc.Get("Nav.About");

    public override string Subtitle => Loc.Get("Page.About");

    public string AppName => AppInfo.DisplayName;

    public string Tagline => AppInfo.Tagline;

    public string Version => Loc.Format("About.Version", AppInfo.VersionString);

    public string Runtime => $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription} · {RuntimeInformation.ProcessArchitecture}";

    public string DataFolder => AppInfo.Paths.Root;

    public IReadOnlyList<ThirdPartyComponent> Components { get; } =
    [
        new(".NET & WPF", "MIT", "https://github.com/dotnet/wpf"),
        new("CommunityToolkit.Mvvm", "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("Microsoft.Extensions.DependencyInjection / Logging", "MIT", "https://github.com/dotnet/runtime")
    ];

    [RelayCommand]
    private void OpenDataFolder() => shell.OpenFolder(AppInfo.Paths.Root);

    [RelayCommand]
    private void OpenLogsFolder() => shell.OpenFolder(AppInfo.Paths.Logs);

    [RelayCommand]
    private void CopyDiagnostics() => shell.CopyToClipboard(
        $"{AppInfo.Name} {AppInfo.VersionString}\n{Runtime}\nData: {AppInfo.Paths.Root}\nLog: {logs.CurrentFile}");

    [RelayCommand]
    private void OpenLink(string url) => shell.OpenUrl(url);

    [RelayCommand]
    private void OpenLegal() => navigation.NavigateTo<LegalViewModel>();

    [RelayCommand]
    private void ViewLogs() => navigation.NavigateTo<LogsViewModel>();

    [RelayCommand]
    private void OpenSecret() => navigation.NavigateTo<MoscowViewModel>();

    [RelayCommand]
    private void Tour() => (System.Windows.Application.Current as App)?.ShowTour();
}
