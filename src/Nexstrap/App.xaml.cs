using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Discord;
using Nexstrap.Services.FastFlags;
using Nexstrap.Services.Integrations;
using Nexstrap.Services.Logging;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Notifications;
using Nexstrap.Services.Performance;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Setup;
using Nexstrap.Services.Shell;
using Nexstrap.Services.Updates;
using Nexstrap.ViewModels;
using Nexstrap.ViewModels.Pages;
using Nexstrap.Views;

namespace Nexstrap;

public partial class App : Application
{
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private EventWaitHandle? _showRequests;

    private const string ShowRequestName = @"Local\Nexstrap.ShowWindow";

    public static IServiceProvider Services => ((App)Current)._services
        ?? throw new InvalidOperationException("Services are not initialised yet.");

    /// <summary>Parsed command line, available to any component.</summary>
    public static StartupOptions Options { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Options = StartupOptions.Parse(e.Args);

        AppInfo.Paths.EnsureCreated();
        _services = ConfigureServices();
        _logger = _services.GetRequiredService<ILogger<App>>();
        HookGlobalExceptionHandlers();
        BindingErrorListener.Attach(_services.GetRequiredService<ILogger<BindingErrorListener>>());

        _logger.LogInformation("{App} {Version} starting on {OS} (.NET {Runtime})",
            AppInfo.Name, AppInfo.VersionString, Environment.OSVersion, Environment.Version);

        var settings = _services.GetRequiredService<ISettingsService>();
        _services.GetRequiredService<IThemeService>().Apply(settings.Current.Appearance);
        Loc.Apply(settings.Current.Language);
        Resources["App.Scale"] = Math.Clamp(settings.Current.Window.Scale, 0.8, 1.5);

        var install = _services.GetRequiredService<IInstallService>();
        if (Options.Uninstall)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            RunUninstall(install);
            Shutdown();
            return;
        }
        install.SyncInstalledVersion();

        // Created eagerly so they're listening for Roblox sessions from the start.
        _services.GetRequiredService<IRichPresenceService>();
        _services.GetRequiredService<ICompanionAppService>();
        _services.GetRequiredService<IPlayHistoryService>();
        _services.GetRequiredService<ILastServerService>();
        _services.GetRequiredService<IServerRegionService>();
        _services.GetRequiredService<IGameJoinAlerts>();
        // Old exe and downloaded update files from a previous self-update.
        _services.GetRequiredService<IUpdateInstaller>().CleanUpAfterUpdate();

        // Windows hands us roblox:/roblox-player: links when Nexstrap is the registered handler.
        if (Options.Positional.FirstOrDefault(RobloxLinks.IsLaunchUri) is { } launchUri)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = HandleLaunchLinkAsync(launchUri);
            return;
        }

        var offerSetup = install.ShouldOfferSetup(Options);
        if (offerSetup && !RunSetup())
            return;

        if (Options.CapturePath is null && !TakeOverFromRunningCopy(canHandOver: !offerSetup && Options is { StartPage: null, JustUpdated: false }))
        {
            Shutdown();
            return;
        }

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        var shell = _services.GetRequiredService<MainWindowViewModel>();
        shell.NavigateToStartPage(Options.StartPage);
        if (Options.CapturePath is null) _services.GetRequiredService<ITrayService>().Show();
        if (!Options.Tray) window.Show();
        _ = shell.CheckForUpdatesOnLaunchAsync();
        // Roblox update notifications shouldn't depend on Home being the first page opened.
        _ = _services.GetRequiredService<IRobloxClientService>().RefreshAsync();

        if (settings.Current.Launcher.CleanOldFilesOnStart)
        {
            var cleanup = _services.GetRequiredService<Nexstrap.Services.Maintenance.ICleanupService>();
            _ = Task.Run(cleanup.CleanOldFiles);
        }
    }

    /// <summary>
    /// A copy waiting in the tray has no window to click, so starting Nexstrap again brings it forward
    /// instead of opening a second copy. Returns false if this copy handed over and should exit.
    /// </summary>
    private bool TakeOverFromRunningCopy(bool canHandOver)
    {
        _showRequests = new EventWaitHandle(false, EventResetMode.AutoReset, ShowRequestName, out var first);
        if (!first && canHandOver)
        {
            _showRequests.Set();
            return false;
        }

        ThreadPool.RegisterWaitForSingleObject(_showRequests,
            (_, _) => Dispatcher.BeginInvoke(ShowMainWindow), null, Timeout.Infinite, executeOnlyOnce: false);
        return true;
    }

    public void ShowMainWindow()
    {
        if (MainWindow is not { } window) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        // Windows won't always let a background app take focus; this brings it above other windows.
        window.Topmost = true;
        window.Topmost = false;
    }

    /// <summary>Shows setup. Returns true if this copy should carry on and open normally.</summary>
    private bool RunSetup()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var setup = _services!.GetRequiredService<SetupViewModel>();
        new SetupWindow(setup).ShowDialog();

        if (setup.Outcome == SetupOutcome.RunPortable)
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            return true;
        }

        if (setup.Outcome == SetupOutcome.OpenInstalled)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup.InstalledExe) { UseShellExecute = false })?.Dispose();
        Shutdown();
        return false;
    }

    private void RunUninstall(IInstallService install)
    {
        var dialogs = _services!.GetRequiredService<IDialogService>();
        if (!install.IsRunningInstalledCopy)
        {
            dialogs.Show(Loc.Get("Uninstall.NotInstalled.Title"), Loc.Get("Uninstall.NotInstalled.Text"));
            return;
        }
        if (install.IsAnotherCopyRunning())
        {
            dialogs.Show(Loc.Get("Uninstall.Running.Title"), Loc.Get("Uninstall.Running.Text"), DialogKind.Warning);
            return;
        }
        if (!dialogs.Confirm(Loc.Get("Uninstall.Title"), Loc.Get("Uninstall.Text"), Loc.Get("Uninstall.Confirm")))
            return;

        var deleteData = dialogs.Confirm(Loc.Get("Uninstall.Data.Title"), Loc.Get("Uninstall.Data.Text"),
            Loc.Get("Uninstall.Data.Delete"), Loc.Get("Uninstall.Data.Keep"));
        try
        {
            install.Uninstall(deleteData);
            dialogs.Show(Loc.Get("Uninstall.Done.Title"), Loc.Get("Uninstall.Done.Text"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        {
            _logger?.LogError(ex, "Uninstall failed");
            dialogs.Show(Loc.Get("Uninstall.Failed.Title"), ex.Message, DialogKind.Error);
        }
    }

    /// <summary>Launches a link without showing the main window, then exits.</summary>
    private async Task HandleLaunchLinkAsync(string uri)
    {
        var services = Services;
        var dialogs = services.GetRequiredService<IDialogService>();
        var logger = services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Handling launch link {Link}", RobloxLinks.Redact(uri));
        try
        {
            var client = services.GetRequiredService<IRobloxClientService>();
            await client.RefreshAsync(checkLatest: false);

            if (client.State.Preferred is not { } installation)
            {
                dialogs.Show(Loc.Get("App.RobloxIsntInstalled"),
                    Loc.Get("App.OpenNexstrapAndChooseInstallRoblox"), DialogKind.Warning);
                return;
            }

            var launchedAt = DateTime.UtcNow;
            var result = services.GetRequiredService<IRobloxLauncher>().LaunchUri(installation, uri);
            if (!result.Success)
            {
                dialogs.Show(Loc.Get("App.RobloxDidntStart"), result.Message, DialogKind.Error);
                return;
            }

            if (result.ProcessId is { } pid)
            {
                // Tracking starts companion apps; staying alive keeps Rich Presence up and lets us close them later.
                var session = services.GetRequiredService<IRobloxSessionService>();
                session.Track(pid, launchedAt);
                if (services.GetRequiredService<IRichPresenceService>().KeepsRunningDuringSession
                    || services.GetRequiredService<ICompanionAppService>().NeedsToWatchSession
                    || services.GetRequiredService<IPlayHistoryService>().IsRecording)
                    await session.WaitForEndAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Launch link failed");
            dialogs.Show(Loc.Get("App.RobloxDidntStart"), Loc.Get("App.SomethingWentWrongWhileStartingRoblox"), DialogKind.Error, ex.ToString());
        }
        finally
        {
            Shutdown();
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        var fileLogger = new FileLoggerProvider(AppInfo.Paths.Logs);
        services.AddSingleton(fileLogger);
        services.AddLogging(b => b.ClearProviders().SetMinimumLevel(LogLevel.Debug).AddProvider(fileLogger));

        // Core services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAppearanceService, AppearanceService>();
        services.AddSingleton<IFontCatalog, FontCatalog>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton(_ => CreateHttpClient(TimeSpan.FromSeconds(20)));
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IUpdateInstaller>(sp => new UpdateInstaller(
            CreateHttpClient(TimeSpan.FromMinutes(10)), sp.GetRequiredService<ILogger<UpdateInstaller>>()));

        // Roblox
        services.AddSingleton<IFastFlagService, FastFlagService>();
        services.AddSingleton<IRobloxFontService, RobloxFontService>();
        services.AddSingleton<IRobloxModService, RobloxModService>();
        services.AddSingleton<IPlayHistoryService, PlayHistoryService>();
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<ILastServerService, LastServerService>();
        services.AddSingleton<IServerRegionService, ServerRegionService>();
        services.AddSingleton<IGameJoinAlerts, GameJoinAlerts>();
        services.AddSingleton<IGameSearchService, GameSearchService>();
        services.AddSingleton<ICardImageUploader>(_ => new CardImageUploader(CreateHttpClient(TimeSpan.FromMinutes(2))));
        services.AddSingleton<IRobloxProcessTuner, RobloxProcessTuner>();
        services.AddSingleton<Nexstrap.Services.Maintenance.ICleanupService, Nexstrap.Services.Maintenance.CleanupService>();
        services.AddSingleton<IWindowsStartup, WindowsStartup>();
        services.AddSingleton<IRobloxLocator, RobloxLocator>();
        services.AddSingleton<IRobloxLauncher, RobloxLauncher>();
        // Large downloads get their own client so the short API timeout doesn't cut them off.
        services.AddSingleton<IRobloxDeployment>(sp => new RobloxDeployment(
            CreateHttpClient(TimeSpan.FromMinutes(30)), sp.GetRequiredService<ILogger<RobloxDeployment>>()));
        services.AddSingleton<IRobloxClientService, RobloxClientService>();
        services.AddSingleton<IExperienceInfoService, ExperienceInfoService>();
        services.AddSingleton<IDisplayInfo, DisplayInfo>();
        services.AddSingleton<IRobloxSessionService, RobloxSessionService>();
        services.AddSingleton<IRobloxUserSettingsService, RobloxUserSettingsService>();
        services.AddSingleton<IProtocolRegistration, ProtocolRegistration>();

        // Discord & integrations
        services.AddSingleton<IDiscordIpcClient, DiscordIpcClient>();
        services.AddSingleton<IRichPresenceService, RichPresenceService>();
        services.AddSingleton<ICompanionAppService, CompanionAppService>();
        services.AddSingleton<INotificationCenter, NotificationCenter>();
        services.AddSingleton<ILaunchScreen, LaunchScreenService>();
        services.AddSingleton<IGameShortcutService, GameShortcutService>();
        services.AddSingleton<ILaunchCoordinator, LaunchCoordinator>();
        services.AddSingleton<IServerBrowserService, ServerBrowserService>();
        services.AddSingleton<IConfigTransferService, ConfigTransferService>();
        services.AddSingleton<IInstallService, InstallService>();
        services.AddSingleton<SetupViewModel>();

        // Pages (singletons so state survives tab switches)
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<AppearanceViewModel>();
        services.AddSingleton<NotificationsViewModel>();
        services.AddSingleton<GamesViewModel>();
        services.AddSingleton<ServersViewModel>();
        services.AddSingleton<LaunchScreenViewModel>();
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<FastFlagsViewModel>();
        services.AddSingleton<RobloxSettingsViewModel>();
        services.AddSingleton<IntegrationsViewModel>();
        services.AddSingleton<AboutViewModel>();

        // Shell
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue(AppInfo.Name, AppInfo.VersionString));
        return client;
    }

    private void HookGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger?.LogCritical(args.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", args.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception");
        e.Handled = true;

        // Keep the app alive and tell the user what happened instead of silently crashing.
        try
        {
            _services?.GetService<IDialogService>()?.Show(
                Loc.Get("App.SomethingWentWrong"),
                Loc.Get("App.NexstrapHitAnUnexpectedErrorYour"),
                DialogKind.Error,
                e.Exception.ToString());
        }
        catch (Exception dialogError)
        {
            _logger?.LogCritical(dialogError, "Could not show the error dialog");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Exiting with code {Code}", e.ApplicationExitCode);
        _services?.GetService<ISettingsService>()?.Save();
        _services?.GetService<IPlayHistoryService>()?.Flush();
        _services?.Dispose();
        _showRequests?.Dispose();
        base.OnExit(e);
    }
}
