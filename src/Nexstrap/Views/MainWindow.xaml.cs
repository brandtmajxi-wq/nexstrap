using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Navigation;
using Nexstrap.ViewModels;

namespace Nexstrap.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(MainWindowViewModel viewModel, ISettingsService settings, INavigationService navigation,
        ILogger<MainWindow> logger)
    {
        _settings = settings;
        _logger = logger;
        InitializeComponent();
        Title = AppInfo.DisplayName;
        Language =System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
        DataContext = viewModel;

        RestoreWindowPlacement();
        StateChanged += (_, _) => UpdateMaximizedState();
        navigation.Navigated += (_, _) => PlayPageTransition();
        ContentRendered += OnContentRendered;
    }

    private void RestoreWindowPlacement()
    {
        var w = _settings.Current.Window;
        Width = Math.Max(MinWidth, Math.Min(w.Width, SystemParameters.WorkArea.Width));
        Height = Math.Max(MinHeight, Math.Min(w.Height, SystemParameters.WorkArea.Height));
        if (w.Maximized) WindowState = WindowState.Maximized;
    }

    private void UpdateMaximizedState()
    {
        var maximized = WindowState == WindowState.Maximized;
        // A maximised chrome-less window overhangs the screen by the resize border; compensate.
        RootGrid.Margin = maximized ? new Thickness(7) : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? Loc.Get("FastFlags.Restore") : Loc.Get("Main.Maximize");
    }

    private void PlayPageTransition()
    {
        PageScroller.ScrollToTop();
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() };
        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        PageHost.BeginAnimation(OpacityProperty, fade);
        PageShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
    }

    private async void OnContentRendered(object? sender, EventArgs e)
    {
        if (App.Options.CapturePath is not { } path) return;

        // Let animations settle before capturing.
        await Task.Delay(600);
        try
        {
            WindowCapture.SavePng(this, path);
            _logger.LogInformation("Captured window to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Window capture failed");
        }
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var w = _settings.Current.Window;
        w.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            w.Width = ActualWidth;
            w.Height = ActualHeight;
        }
        _settings.Save();

        // Application.Shutdown ignores this, so Quit and updates still close the window.
        if (_settings.Current.Launcher.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
