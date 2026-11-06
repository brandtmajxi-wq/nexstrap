using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Shell;

namespace Nexstrap.ViewModels.Pages;

/// <summary>The privacy policy and terms of use, opened from About.</summary>
public sealed partial class LegalViewModel(IShellService shell, INavigationService navigation) : PageViewModel
{
    public override string Title => Loc.Get("Legal.Title");

    public override string Subtitle => Loc.Get("Legal.Updated");

    [RelayCommand]
    private void Back() => navigation.NavigateTo<AboutViewModel>();

    [RelayCommand]
    private void OpenIssues() => shell.OpenUrl(AppInfo.RepositoryUrl + "/issues");

    [RelayCommand]
    private void OpenLicense() => shell.OpenUrl(AppInfo.RepositoryUrl + "/blob/main/LICENSE");
}
