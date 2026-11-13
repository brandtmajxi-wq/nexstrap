using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Navigation;
using Nexstrap.ViewModels;

namespace Nexstrap.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(MainWindowViewModel viewModel, ISettingsService settings, INavigationService navigation,
        ILogger<MainWindow> logger, IAppearanceService appearance)
    {
        _settings = settings;
        _logger = logger;
        InitializeComponent();
        Title = AppInfo.DisplayName;
        Language =System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
        DataContext = viewModel;

        ApplyBackgroundBlur();
        appearance.Changed += (_, _) => ApplyBackgroundBlur();
        LowEndMode.Changed += (_, _) => Dispatcher.BeginInvoke(ApplyBackgroundBlur);
        RestoreWindowPlacement();
        if (App.Options.CapturePath is not null)
        {
            // Screenshots for checking pages render off-screen, so they never pop up over a game.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.VirtualScreenLeft - Width - 200;
            Top = SystemParameters.VirtualScreenTop;
            ShowActivated = false;
            ShowInTaskbar = false;
        }
        StateChanged += (_, _) => UpdateMaximizedState();
        navigation.Navigated += (_, _) => PlayPageTransition();
        viewModel.ScrollToSetting += (_, label) => Dispatcher.BeginInvoke(() => FindCard(PageScroller, label)?.BringIntoView(),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        PreviewKeyDown += (_, e) =>
        {
            var (action, page) = Shortcuts.Match(e.Key, System.Windows.Input.Keyboard.Modifiers);
            switch (action)
            {
                case ShortcutAction.Search: SettingsSearch.Focus(); break;
                case ShortcutAction.Page: if (!viewModel.OpenSidebarPage(page)) return; break;
                case ShortcutAction.Play: viewModel.PlayFromShortcut(); break;
                case ShortcutAction.Rejoin: viewModel.RejoinFromShortcut(); break;
                case ShortcutAction.Notifications: navigation.NavigateTo<Nexstrap.ViewModels.Pages.NotificationsViewModel>(); break;
                default: return;
            }
            e.Handled = true;
        };
        ContentRendered += OnContentRendered;
    }

    /// <summary>The layer is made larger than the window so the blur doesn't soften its edges into the window colour.</summary>
    private void ApplyBackgroundBlur()
    {
        // Blur is one of the most expensive effects, so low-end mode turns it off.
        var radius = LowEndMode.On ? 0 : _settings.Current.Appearance.BackgroundBlur;
        BackgroundLayer.Effect = radius > 0 ? new System.Windows.Media.Effects.BlurEffect { Radius = radius } : null;
        BackgroundLayer.Margin = new Thickness(-radius * 2);
    }

    private static Nexstrap.Controls.SettingCard? FindCard(DependencyObject parent, string header)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Nexstrap.Controls.SettingCard card && card.Header == header) return card;
            if (FindCard(child, header) is { } found) return found;
        }
        return null;
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
        var style = _settings.Current.Appearance.PageTransition;
        if (style == PageTransition.None || LowEndMode.On) return;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() };
        PageHost.BeginAnimation(OpacityProperty, fade);
        if (style != PageTransition.Slide) return;

        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
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
