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
using Nexstrap.Services.Performance;
using Nexstrap.Services.Roblox;
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
        // Created eagerly so they're listening for Roblox sessions from the start.
        _services.GetRequiredService<IRichPresenceService>();
        _services.GetRequiredService<ICompanionAppService>();
        // Old exe and downloaded update files from a previous self-update.
        _services.GetRequiredService<IUpdateInstaller>().CleanUpAfterUpdate();

        // Windows hands us roblox:/roblox-player: links when Nexstrap is the registered handler.
        if (Options.Positional.FirstOrDefault(RobloxLinks.IsLaunchUri) is { } launchUri)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = HandleLaunchLinkAsync(launchUri);
            return;
        }

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        var shell = _services.GetRequiredService<MainWindowViewModel>();
        shell.NavigateToStartPage(Options.StartPage);
        window.Show();
        _ = shell.CheckForUpdatesOnLaunchAsync();
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
                dialogs.Show("Roblox isn't installed",
                    "Open Nexstrap and choose Install Roblox, then try the link again.", DialogKind.Warning);
                return;
            }

            var launchedAt = DateTime.UtcNow;
            var result = services.GetRequiredService<IRobloxLauncher>().LaunchUri(installation, uri);
            if (!result.Success)
            {
                dialogs.Show("Roblox didn't start", result.Message, DialogKind.Error);
                return;
            }

            if (result.ProcessId is { } pid)
            {
                // Tracking starts companion apps; staying alive keeps Rich Presence up and lets us close them later.
                var session = services.GetRequiredService<IRobloxSessionService>();
                session.Track(pid, launchedAt);
                if (services.GetRequiredService<IRichPresenceService>().KeepsRunningDuringSession
                    || services.GetRequiredService<ICompanionAppService>().NeedsToWatchSession)
                    await session.WaitForEndAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Launch link failed");
            dialogs.Show("Roblox didn't start", "Something went wrong while starting Roblox.", DialogKind.Error, ex.ToString());
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
        services.AddSingleton<IConfigTransferService, ConfigTransferService>();

        // Pages (singletons so state survives tab switches)
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<AppearanceViewModel>();
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
                "Something went wrong",
                "Nexstrap hit an unexpected error. Your settings are safe. The details were written to the log file.",
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
        _services?.Dispose();
        base.OnExit(e);
    }
}
