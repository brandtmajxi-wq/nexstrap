using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexstrap.ViewModels;

namespace Nexstrap.Services.Navigation;

public interface INavigationService
{
    PageViewModel? CurrentPage { get; }

    void NavigateTo<TPage>() where TPage : PageViewModel;

    void NavigateTo(Type pageType);

    event EventHandler<PageViewModel>? Navigated;
}

/// <summary>
/// ViewModel-first navigation. Pages are DI singletons so their state survives
/// switching tabs; views are resolved from DataTemplates in App.xaml.
/// </summary>
public sealed class NavigationService(IServiceProvider services, ILogger<NavigationService> logger)
    : ObservableObject, INavigationService
{
    private PageViewModel? _currentPage;

    public PageViewModel? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public event EventHandler<PageViewModel>? Navigated;

    public void NavigateTo<TPage>() where TPage : PageViewModel => NavigateTo(typeof(TPage));

    public void NavigateTo(Type pageType)
    {
        if (CurrentPage?.GetType() == pageType) return;

        var page = (PageViewModel)services.GetRequiredService(pageType);
        CurrentPage?.OnNavigatedFrom();
        CurrentPage = page;
        page.OnNavigatedTo();
        logger.LogDebug("Navigated to {Page}", pageType.Name);
        Navigated?.Invoke(this, page);
    }
}
