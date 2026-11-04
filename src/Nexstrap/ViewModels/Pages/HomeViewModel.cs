using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Shell;
using Nexstrap.Services.Updates;

namespace Nexstrap.ViewModels.Pages;

/// <summary>A row in the installations list.</summary>
public sealed class InstallationItem(RobloxInstallation installation, bool isUsedForPlay, bool isLatest)
{
    public RobloxInstallation Installation { get; } = installation;
    public bool IsUsedForPlay { get; } = isUsedForPlay;
    public bool IsLatest { get; } = isLatest;
    public bool CanRemove => Installation.Source == InstallationSource.Nexstrap;
}

public sealed partial class HomeViewModel : PageViewModel
{
    private const int MaxRecent = 6;

    private readonly IRobloxClientService _client;
    private readonly ILaunchCoordinator _coordinator;
    private readonly IExperienceInfoService _experiences;
    private readonly IUpdateService _updates;
    private readonly ISettingsService _settings;
    private readonly IAppearanceService _appearance;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly IRobloxSessionService _session;
    private readonly IUpdateInstaller _installer;
    private CancellationTokenSource? _updateCts;
    private readonly ILogger<HomeViewModel> _logger;
    private CancellationTokenSource? _installCts;
    private CancellationTokenSource? _lookupCts;
    private bool _initialised;

    public HomeViewModel(IRobloxClientService client, ILaunchCoordinator coordinator, IExperienceInfoService experiences,
        IUpdateService updates, ISettingsService settings, IAppearanceService appearance, IDialogService dialogs,
        IShellService shell, IRobloxSessionService session, IUpdateInstaller installer, ILogger<HomeViewModel> logger)
    {
        _installer = installer;
        _session = session;
        _client = client;
        _coordinator = coordinator;
        _experiences = experiences;
        _updates = updates;
        _settings = settings;
        _appearance = appearance;
        _dialogs = dialogs;
        _shell = shell;
        _logger = logger;

        if (JustUpdatedMessage is not null) UpdateMessage = JustUpdatedMessage;
        updates.Checked += (_, result) => Application.Current?.Dispatcher.BeginInvoke(() => ApplyUpdateResult(result));
        appearance.Changed += (_, _) => OnPropertyChanged(nameof(IsLightTheme));
        client.StateChanged += (_, _) => Application.Current?.Dispatcher.Invoke(OnClientStateChanged);
        coordinator.RecentsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(LoadRecent);
        LoadRecent();
    }

    public override string Title => Greeting();

    public override string Subtitle => Loc.Get("Tagline");

    // ===== Roblox client state =====

    private ClientState State => _client.State;

    public RobloxInstallation? Installation => State.Preferred;

    public ObservableCollection<InstallationItem> Installations { get; } = [];

    public bool IsInstalled => Installation is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTitle), nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(JoinCommand), nameof(InstallCommand), nameof(RefreshCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(JoinCommand), nameof(InstallCommand), nameof(RefreshCommand), nameof(RemoveInstallationCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(JoinCommand))]
    private bool _isLaunching;

    public bool IsBusy => IsScanning || IsInstalling;

    [ObservableProperty]
    private string? _launchMessage;

    public string StatusTitle => IsScanning ? "Checking…" : IsInstalled ? "Roblox is installed" : "Roblox not found";

    public string StatusDetail => IsInstalled
        ? Installation!.Directory
        : "No Roblox Player build was found. Install it with Nexstrap or from roblox.com.";

    public string VersionText => Installation?.DisplayVersion ?? "—";

    public string SourceText => Installation?.SourceLabel ?? "—";

    public string ProtocolOwner => State.ProtocolHandler.OwnerName;

    public bool UpdateAvailable => State.UpdateAvailable && IsInstalled;

    /// <summary>Roblox's live version has been fetched, so "outdated" labels are meaningful.</summary>
    public bool LatestKnown => State.Latest is not null;

    public string LatestText => State.Latest is { } l
        ? State.IsUpToDate ? "Up to date with Roblox" : $"Roblox {l.Version} is available"
        : State.LatestError ?? "Latest version not checked";

    /// <summary>Hero button: Install when nothing is there, Update when outdated.</summary>
    public string InstallButtonText => IsInstalled ? "Update Roblox" : "Install Roblox";

    public bool ShowInstallButton => !IsInstalled || UpdateAvailable;

    /// <summary>FastFlags only apply to Nexstrap's own builds, so offer one even when another launcher's is current.</summary>
    public bool CanAddNexstrapCopy => !State.Installations.Any(i => i.Source == InstallationSource.Nexstrap);

    public bool IsAutomaticSelection
    {
        get => _client.IsAutomaticSelection;
        set { if (value) _client.SetPreferred(null); }
    }

    // ===== Install progress =====

    [ObservableProperty]
    private double _installFraction;

    [ObservableProperty]
    private string _installStage = "";

    [ObservableProperty]
    private string _installDetail = "";

    // ===== Experiences =====

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    [NotifyPropertyChangedFor(nameof(ExperienceInputError))]
    private string _experienceInput = "";

    [ObservableProperty]
    private ExperienceInfo? _experience;

    [ObservableProperty]
    private string? _experienceStatus;

    public string? ExperienceInputError =>
        string.IsNullOrWhiteSpace(ExperienceInput) || RobloxLinks.TryParsePlaceId(ExperienceInput, out _)
            ? null
            : "Enter a place ID or a roblox.com/games/… link.";

    public ObservableCollection<RecentExperience> RecentExperiences { get; } = [];

    public bool HasRecent => RecentExperiences.Count > 0;

    partial void OnExperienceInputChanged(string value) => _ = LookupExperienceAsync(value);

    // ===== Quick settings =====

    public bool IsLightTheme
    {
        get => _appearance.Current.Theme == AppTheme.Light;
        set
        {
            var theme = value ? AppTheme.Light : AppTheme.Dark;
            if (_appearance.Current.Theme != theme) _appearance.SetTheme(theme);
        }
    }

    public bool CloseAfterLaunch
    {
        get => _settings.Current.Launcher.CloseAfterLaunch;
        set
        {
            _settings.Current.Launcher.CloseAfterLaunch = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _settings.Current.Launcher.CheckForUpdatesOnStartup;
        set
        {
            _settings.Current.Launcher.CheckForUpdatesOnStartup = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    // ===== Nexstrap updates =====

    public string CurrentVersion => $"v{AppInfo.VersionString}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private string _updateMessage = "Not checked yet.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenReleaseCommand), nameof(InstallUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable), nameof(ReleaseNotes), nameof(HasReleaseNotes))]
    private UpdateCheckResult? _updateResult;

    public bool IsUpdateAvailable => UpdateResult?.Status == UpdateStatus.UpdateAvailable;

    public string? ReleaseNotes => IsUpdateAvailable ? UpdateResult?.ReleaseNotes?.Trim() : null;

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand), nameof(CheckForUpdatesCommand))]
    private bool _isUpdating;

    [ObservableProperty]
    private double _updateFraction;

    [ObservableProperty]
    private string _updateStage = "";

    /// <summary>Set when this instance was started by the updater.</summary>
    public string? JustUpdatedMessage { get; } = App.Options.JustUpdated ? $"Nexstrap was updated to v{AppInfo.VersionString}." : null;

    // ===== Lifecycle =====

    public override void OnNavigatedTo()
    {
        OnPropertyChanged(nameof(Title));
        if (_initialised) return;
        _initialised = true;

        // The Nexstrap update check runs at app launch (MainWindowViewModel); its result arrives via _updates.Checked.
        _ = RefreshAsync();
        if (_updates.LastResult is { } last) ApplyUpdateResult(last);
    }

    private void ApplyUpdateResult(UpdateCheckResult result)
    {
        UpdateResult = result;
        if (!IsUpdating) UpdateMessage = result.Message;
    }

    private void OnClientStateChanged()
    {
        Installations.Clear();
        foreach (var i in State.Installations)
            Installations.Add(new InstallationItem(i, i == State.Preferred, i.VersionHash == State.Latest?.VersionHash));

        foreach (var name in new[]
                 {
                     nameof(Installation), nameof(IsInstalled), nameof(StatusTitle), nameof(StatusDetail), nameof(VersionText),
                     nameof(SourceText), nameof(ProtocolOwner), nameof(UpdateAvailable), nameof(LatestText),
                     nameof(InstallButtonText), nameof(ShowInstallButton), nameof(IsAutomaticSelection), nameof(LatestKnown), nameof(CanAddNexstrapCopy)
                 })
            OnPropertyChanged(name);

        PlayCommand.NotifyCanExecuteChanged();
        JoinCommand.NotifyCanExecuteChanged();
        OpenInstallFolderCommand.NotifyCanExecuteChanged();
    }

    // ===== Commands: installation =====

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsScanning = true;
        try
        {
            await _client.RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Installation scan failed");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanInstall() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        IsInstalling = true;
        InstallFraction = 0;
        InstallStage = "Preparing…";
        InstallDetail = "";
        _installCts = new CancellationTokenSource();

        var progress = new Progress<InstallProgress>(p =>
        {
            InstallStage = p.Stage;
            InstallFraction = p.Fraction;
            InstallDetail = p.BytesTotal > 0 ? $"{FormatBytes(p.BytesDone)} of {FormatBytes(p.BytesTotal)}" : "";
        });

        try
        {
            var installed = await _client.InstallLatestAsync(progress, _installCts.Token);
            LaunchMessage = $"Roblox {installed.DisplayVersion} is ready.";
        }
        catch (OperationCanceledException)
        {
            LaunchMessage = "Installation cancelled. Nothing was changed.";
        }
        catch (InstallException ex)
        {
            _dialogs.Show("Roblox couldn't be installed", ex.Message, DialogKind.Error, ex.InnerException?.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            _logger.LogError(ex, "Install failed");
            _dialogs.Show("Roblox couldn't be installed", "A file or network error stopped the installation: " + ex.Message, DialogKind.Error, ex.ToString());
        }
        finally
        {
            _installCts.Dispose();
            _installCts = null;
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void CancelInstall() => _installCts?.Cancel();

    [RelayCommand]
    private void UseForPlay(InstallationItem item) => _client.SetPreferred(item.Installation);

    private bool CanRemoveInstallation(InstallationItem? item) => item?.CanRemove == true && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanRemoveInstallation))]
    private void RemoveInstallation(InstallationItem item)
    {
        if (!_dialogs.Confirm("Remove this Roblox build?",
                $"Roblox {item.Installation.DisplayVersion} will be deleted from Nexstrap's folder. You can install it again at any time.", "Remove"))
            return;
        try
        {
            _client.Remove(item.Installation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _dialogs.Show("Couldn't remove Roblox", ex.Message, DialogKind.Warning);
        }
    }

    [RelayCommand]
    private void OpenFolderOf(InstallationItem item) => _shell.OpenFolder(item.Installation.Directory);

    private bool CanOpenInstallFolder() => IsInstalled;

    [RelayCommand(CanExecute = nameof(CanOpenInstallFolder))]
    private void OpenInstallFolder() => _shell.OpenFolder(Installation!.Directory);

    // ===== Commands: launching =====

    private bool CanPlay() => IsInstalled && !IsLaunching && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private Task PlayAsync() => LaunchAsync(_coordinator.LaunchAppAsync);

    private bool CanJoin() => CanPlay() && RobloxLinks.TryParsePlaceId(ExperienceInput, out _);

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private Task JoinAsync()
    {
        RobloxLinks.TryParsePlaceId(ExperienceInput, out var placeId);
        return JoinPlaceAsync(placeId, Experience?.PlaceId == placeId ? Experience.Name : null);
    }

    [RelayCommand]
    private Task JoinRecentAsync(RecentExperience recent) => JoinPlaceAsync(recent.PlaceId, recent.Name);

    [RelayCommand]
    private void ClearRecent()
    {
        _settings.Current.Launcher.RecentExperiences.Clear();
        _settings.Save();
        LoadRecent();
    }

    private Task JoinPlaceAsync(long placeId, string? name) =>
        CanPlay() ? LaunchAsync(() => _coordinator.JoinAsync(placeId, name)) : Task.CompletedTask;

    private async Task LaunchAsync(Func<Task<LaunchResult?>> launch)
    {
        IsLaunching = true;
        LaunchMessage = "Starting Roblox…";
        try
        {
            if (await launch() is { Success: true } result)
            {
                LaunchMessage = result.Message;
                await Task.Delay(4000);
            }
            LaunchMessage = null;
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private async Task LookupExperienceAsync(string input)
    {
        _lookupCts?.Cancel();
        Experience = null;
        ExperienceStatus = null;
        if (!RobloxLinks.TryParsePlaceId(input, out var placeId)) return;

        var cts = _lookupCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(350, cts.Token); // debounce typing
            ExperienceStatus = "Looking up…";
            var info = await _experiences.LookupAsync(placeId, cts.Token);
            if (cts.IsCancellationRequested) return;
            Experience = info;
            ExperienceStatus = info is null ? "No experience found with that place ID." : null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _logger.LogDebug(ex, "Experience lookup failed");
            if (!cts.IsCancellationRequested) ExperienceStatus = "Couldn't look up the name, but you can still join.";
        }
    }

    private void LoadRecent()
    {
        RecentExperiences.Clear();
        foreach (var r in _settings.Current.Launcher.RecentExperiences.Take(MaxRecent)) RecentExperiences.Add(r);
        OnPropertyChanged(nameof(HasRecent));
    }

    // ===== Commands: Nexstrap updates =====

    private bool CanInstallUpdate() => IsUpdateAvailable && !IsUpdating && UpdateResult?.Release is not null;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (_session.IsActive && !_dialogs.Confirm("Roblox is running",
                "Roblox keeps running, but Discord Rich Presence and companion-app closing pause until you start Roblox from Nexstrap again. Update now?",
                "Update now", "Later"))
            return;

        IsUpdating = true;
        _updateCts = new CancellationTokenSource();
        var progress = new Progress<UpdateProgress>(p =>
        {
            UpdateStage = p.BytesTotal > 0 ? $"{p.Stage} {FormatBytes(p.BytesDone)} of {FormatBytes(p.BytesTotal)}" : p.Stage;
            UpdateFraction = p.Fraction;
        });

        try
        {
            var verified = await _installer.DownloadAndVerifyAsync(UpdateResult!.Release!, progress, _updateCts.Token);
            UpdateStage = "Verified. Restarting Nexstrap…";
            _settings.Save();
            _installer.InstallAndRestart(verified);

            // Not Application.Shutdown(): a single-file app loads assemblies from its own exe on demand,
            // and that file is now the new version, so normal shutdown code would fail to load.
            Environment.Exit(0);
        }
        catch (OperationCanceledException)
        {
            UpdateMessage = "Update cancelled. Nothing was changed.";
        }
        catch (UpdateException ex)
        {
            UpdateMessage = "The update wasn't installed.";
            _dialogs.Show("Update not installed", ex.Message, DialogKind.Warning, ex.InnerException?.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UpdateMessage = "The update wasn't installed.";
            _dialogs.Show("Update not installed", "A file error stopped the update: " + ex.Message, DialogKind.Error, ex.ToString());
        }
        finally
        {
            _updateCts?.Dispose();
            _updateCts = null;
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void CancelUpdate() => _updateCts?.Cancel();

    private bool CanCheckForUpdates() => !IsCheckingUpdates && !IsUpdating;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingUpdates = true;
        UpdateMessage = "Checking GitHub…";
        try
        {
            ApplyUpdateResult(await _updates.CheckAsync());
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    private bool CanOpenRelease() => UpdateResult?.ReleaseUrl is not null;

    [RelayCommand(CanExecute = nameof(CanOpenRelease))]
    private void OpenRelease() => _shell.OpenUrl(UpdateResult!.ReleaseUrl!);

    // ===== Helpers =====

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B"
    };

    private static string Greeting() => DateTime.Now.Hour switch
    {
        < 5 => Loc.Get("Home.Night"),
        < 12 => Loc.Get("Home.Morning"),
        < 18 => Loc.Get("Home.Afternoon"),
        _ => Loc.Get("Home.Evening")
    };
}
