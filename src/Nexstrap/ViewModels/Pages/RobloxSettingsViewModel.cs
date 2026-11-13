using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Maintenance;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Setup;
using Nexstrap.Services.Shell;

namespace Nexstrap.ViewModels.Pages;

public sealed partial class RobloxSettingsViewModel : PageViewModel
{
    /// <summary>The caps Roblox's in-game Maximum Frame Rate menu offers.</summary>
    internal static readonly int[] StandardFramerates = [60, 120, 144, 240];

    private readonly IRobloxUserSettingsService _robloxSettings;
    private readonly IRobloxLauncher _launcher;
    private readonly IRobloxClientService _client;
    private readonly IProtocolRegistration _protocol;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly INavigationService _navigation;
    private readonly IWindowsStartup _startup;
    private readonly IHotkeyService _hotkeys;
    private readonly ICleanupService _cleanup;
    private RobloxUserSettings _loaded = new();
    private bool _loading;

    private readonly ISessionComforts _comforts;
    private readonly IBackgroundMute _backgroundMute;

    public bool MuteRobloxInBackground
    {
        get => _settings.Current.Launcher.MuteRobloxInBackground;
        set
        {
            _settings.Current.Launcher.MuteRobloxInBackground = value;
            _settings.Save();
            _backgroundMute.Refresh();
            OnPropertyChanged();
        }
    }

    public RobloxSettingsViewModel(IRobloxUserSettingsService robloxSettings, IRobloxLauncher launcher, IRobloxClientService client,
        IProtocolRegistration protocol, ISettingsService settings, IDialogService dialogs, IShellService shell, INavigationService navigation,
        IWindowsStartup startup, ICleanupService cleanup, IHotkeyService hotkeys, ISessionComforts comforts, IBackgroundMute backgroundMute)
    {
        _comforts = comforts;
        _backgroundMute = backgroundMute;
        _hotkeys = hotkeys;
        _robloxSettings = robloxSettings;
        _launcher = launcher;
        _client = client;
        _protocol = protocol;
        _settings = settings;
        _dialogs = dialogs;
        _shell = shell;
        _navigation = navigation;
        _startup = startup;
        _cleanup = cleanup;
        client.StateChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(InstallationText));
            OnPropertyChanged(nameof(LinkHandlerText));
        });
    }

    public override string Title => Loc.Get("Nav.RobloxSettings");

    public override string Subtitle => Loc.Get("Page.RobloxSettings");

    // ===== Status =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(EditBlockedReason))]
    private bool _robloxRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(EditBlockedReason))]
    private bool _settingsFileExists;

    public bool CanEdit => SettingsFileExists && !RobloxRunning;

    public string? EditBlockedReason =>
        !SettingsFileExists ? Loc.Get("Performance.RobloxHasntCreatedItsSettingsFile")
        : RobloxRunning ? Loc.Get("RobloxSettings.RobloxIsOpenCloseItTo")
        : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DiscardCommand))]
    private bool _hasChanges;

    [ObservableProperty]
    private string? _statusMessage;

    // ===== In-game settings (pending until Save) =====

    public ObservableCollection<Choice<int>> FramerateChoices { get; } = [];

    [ObservableProperty]
    private int _framerateCap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityLabel))]
    private bool _qualityAutomatic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityLabel))]
    private double _qualityLevel = 5;

    public string QualityLabel => QualityAutomatic ? Loc.Get("Performance.Automatic") : Loc.Format("RobloxSettings.10", QualityLevel);

    [ObservableProperty]
    private bool _fullscreen;

    [ObservableProperty]
    private double _volumePercent;

    [ObservableProperty]
    private double _mouseSensitivity = 1;

    [ObservableProperty]
    private bool _invertCamera;

    partial void OnFramerateCapChanged(int value) => MarkChanged();
    partial void OnQualityAutomaticChanged(bool value) => MarkChanged();
    partial void OnQualityLevelChanged(double value) => MarkChanged();
    partial void OnFullscreenChanged(bool value) => MarkChanged();
    partial void OnVolumePercentChanged(double value) => MarkChanged();
    partial void OnMouseSensitivityChanged(double value) => MarkChanged();
    partial void OnInvertCameraChanged(bool value) => MarkChanged();

    private void MarkChanged()
    {
        if (_loading) return;
        HasChanges = true;
        StatusMessage = null;
    }

    private bool CanSave() => HasChanges;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        RefreshStatus();
        if (!CanEdit)
        {
            _dialogs.Show(Loc.Get("RobloxSettings.CantSaveRightNow"), EditBlockedReason!, DialogKind.Warning);
            return;
        }

        try
        {
            _robloxSettings.Save(BuildChanges());
            Load();
            StatusMessage = Loc.Get("RobloxSettings.SavedRobloxUsesTheseTheNext");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            _dialogs.Show(Loc.Get("RobloxSettings.CouldntSaveRobloxsSettings"), ex.Message, DialogKind.Error);
        }
    }

    /// <summary>Only values that differ from the file are written.</summary>
    internal RobloxUserSettings BuildChanges()
    {
        var quality = QualityAutomatic ? 0 : (int)Math.Round(QualityLevel);
        var volume = Math.Round(VolumePercent) / 100.0;
        var sensitivity = Math.Round(MouseSensitivity, 1);
        return new RobloxUserSettings
        {
            FramerateCap = FramerateCap != _loaded.FramerateCap ? FramerateCap : null,
            SavedQualityLevel = quality != _loaded.SavedQualityLevel ? quality : null,
            Fullscreen = Fullscreen != _loaded.Fullscreen ? Fullscreen : null,
            MasterVolume = Math.Abs(volume - (_loaded.MasterVolume ?? -1)) > 0.004 ? volume : null,
            MouseSensitivity = Math.Abs(sensitivity - (_loaded.MouseSensitivity ?? -1)) > 0.04 ? sensitivity : null,
            CameraYInverted = InvertCamera != _loaded.CameraYInverted ? InvertCamera : null
        };
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Discard()
    {
        Load();
        StatusMessage = Loc.Get("RobloxSettings.ChangesDiscarded");
    }

    // ===== Launch preferences =====

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

    /// <summary>Links and startup belong to the installed copy; the dev copy must not take them over.</summary>
    public bool CanChangeWindowsIntegration => !AppInfo.IsDev;

    public bool UseNexstrapForLinks
    {
        get => _protocol.IsRegistered;
        set
        {
            if (AppInfo.IsDev) return;
            try
            {
                if (value) _protocol.Register();
                else _protocol.Unregister();
                _ = _client.RefreshAsync(checkLatest: false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                _dialogs.Show(Loc.Get("RobloxSettings.CouldntChangeTheLinkHandler"), ex.Message, DialogKind.Error);
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkHandlerText));
        }
    }

    public bool StartWithWindows
    {
        get => _startup.IsEnabled;
        set
        {
            if (AppInfo.IsDev) return;
            try
            {
                if (value) _startup.Enable(StartHidden);
                else _startup.Disable();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                _dialogs.Show(Loc.Get("RobloxSettings.CouldntChangeStartup"), ex.Message, DialogKind.Error);
            }
            OnPropertyChanged();
        }
    }

    public bool AutoUpdateRoblox
    {
        get => _settings.Current.Launcher.AutoUpdateRoblox;
        set
        {
            _settings.Current.Launcher.AutoUpdateRoblox = value;
            _settings.Save();
            OnPropertyChanged();
            if (value) _ = _client.RefreshAsync();
        }
    }

    public bool KeepAwakeWhilePlaying
    {
        get => _settings.Current.Launcher.KeepAwakeWhilePlaying;
        set
        {
            _settings.Current.Launcher.KeepAwakeWhilePlaying = value;
            _settings.Save();
            _comforts.Refresh();
            OnPropertyChanged();
        }
    }

    public bool GlobalHotkeys
    {
        get => _settings.Current.Launcher.GlobalHotkeys;
        set
        {
            var result = _hotkeys.Apply(value);
            _settings.Current.Launcher.GlobalHotkeys = value && result != HotkeyResult.NoneWorking;
            _settings.Save();
            OnPropertyChanged();
            // After the toggle has updated, because this dialog waits for an answer.
            if (result != HotkeyResult.AllWorking)
                Application.Current?.Dispatcher.BeginInvoke(() => _dialogs.Show(Loc.Get("RobloxSettings.Hotkeys.Taken"), Loc.Get("RobloxSettings.Hotkeys.Taken.Text"), DialogKind.Warning));
        }
    }

    public bool StartHidden
    {
        get => _settings.Current.Launcher.StartHidden;
        set
        {
            _settings.Current.Launcher.StartHidden = value;
            _settings.Save();
            if (StartWithWindows) StartWithWindows = true; // rewrites the entry with the new choice
            OnPropertyChanged();
        }
    }

    public bool CloseToTray
    {
        get => _settings.Current.Launcher.CloseToTray;
        set
        {
            _settings.Current.Launcher.CloseToTray = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<Choice<RobloxPriority>> PriorityChoices { get; } =
    [
        new(RobloxPriority.Automatic, () => Loc.Get("RobloxSettings.Priority.Automatic")),
        new(RobloxPriority.BelowNormal, () => Loc.Get("RobloxSettings.Priority.BelowNormal")),
        new(RobloxPriority.AboveNormal, () => Loc.Get("RobloxSettings.Priority.AboveNormal")),
        new(RobloxPriority.High, () => Loc.Get("RobloxSettings.Priority.High"))
    ];

    public RobloxPriority RobloxPriority
    {
        get => _settings.Current.Launcher.RobloxPriority;
        set
        {
            _settings.Current.Launcher.RobloxPriority = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<Choice<int>> CoreChoices { get; } = BuildCoreChoices(Environment.ProcessorCount);

    internal static IReadOnlyList<Choice<int>> BuildCoreChoices(int processors)
    {
        var choices = new List<Choice<int>> { new(0, () => Loc.Get("RobloxSettings.AllCores")) };
        for (var cores = 2; cores < processors; cores += 2)
        {
            var count = cores;
            choices.Add(new(count, () => Loc.Format("RobloxSettings.CoreCount", count)));
        }
        return choices;
    }

    public int RobloxCoreLimit
    {
        get => CoreChoices.Any(c => c.Value == _settings.Current.Launcher.RobloxCoreLimit) ? _settings.Current.Launcher.RobloxCoreLimit : 0;
        set
        {
            _settings.Current.Launcher.RobloxCoreLimit = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public string RobloxWindowTitle
    {
        get => _settings.Current.Launcher.RobloxWindowTitle ?? "";
        set
        {
            _settings.Current.Launcher.RobloxWindowTitle = RobloxProcessTuner.CleanTitle(value);
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public string LinkHandlerText => _protocol.IsRegistered
        ? Loc.Get("RobloxSettings.RobloxComPlayButtonsOpenNexstrap")
        : Loc.Format("RobloxSettings.CurrentlyHandledByTurningThisOn", _client.State.ProtocolHandler.OwnerName);

    // ===== Cleanup =====

    public bool CleanOldFilesOnStart
    {
        get => _settings.Current.Launcher.CleanOldFilesOnStart;
        set
        {
            _settings.Current.Launcher.CleanOldFilesOnStart = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private string? _cleanupResult;

    public string CleanupDescription => CleanupResult ?? Loc.Get("RobloxSettings.CleanNow.Text");

    partial void OnCleanupResultChanged(string? value) => OnPropertyChanged(nameof(CleanupDescription));

    [RelayCommand]
    private async Task CleanNowAsync()
    {
        if (_launcher.IsRobloxRunning())
        {
            CleanupResult = Loc.Get("RobloxSettings.CleanCloseRoblox");
            return;
        }
        var result = await Task.Run(_cleanup.CleanOldFiles);
        CleanupResult = Loc.Format("RobloxSettings.Cleaned", result.Files, result.Bytes / 1024d / 1024d);
    }

    // ===== Installation =====

    public string InstallationText => _client.State.Preferred is { } p
        ? Loc.Format("RobloxSettings.PlayUsesRoblox", p.DisplayVersion, p.SourceLabel)
        : Loc.Get("RobloxSettings.NoRobloxBuildFound");

    [RelayCommand]
    private void ManageInstallations() => _navigation.NavigateTo<HomeViewModel>();

    // ===== Backups =====

    public ObservableCollection<SettingsBackup> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    [RelayCommand]
    private void BackupNow()
    {
        try
        {
            var b = _robloxSettings.BackupNow();
            StatusMessage = Loc.Format("RobloxSettings.BackedUpRobloxsSettings", b.CreatedLocal);
            RefreshBackups();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("RobloxSettings.CouldntBackUp"), ex.Message, DialogKind.Warning);
        }
    }

    [RelayCommand]
    private void Restore(SettingsBackup backup)
    {
        if (!_dialogs.Confirm(Loc.Get("RobloxSettings.RestoreRobloxsSettings"),
                Loc.Format("RobloxSettings.RobloxsSettingsWillGoBackTo", backup.CreatedLocal), Loc.Get("FastFlags.Restore")))
            return;
        try
        {
            _robloxSettings.Restore(backup);
            Load();
            StatusMessage = Loc.Format("FastFlags.RestoredTheBackupFrom", backup.CreatedLocal);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            _dialogs.Show(Loc.Get("FastFlags.CouldntRestore"), ex.Message, DialogKind.Error);
        }
    }

    // ===== Advanced =====

    [RelayCommand]
    private void OpenSettingsFolder() => _shell.OpenFolder(Path.GetDirectoryName(_robloxSettings.FilePath)!);

    [RelayCommand]
    private void OpenRobloxLogs() =>
        _shell.OpenFolder(Path.Combine(Path.GetDirectoryName(_robloxSettings.FilePath)!, "logs"));

    [RelayCommand]
    private void Refresh()
    {
        if (HasChanges && !_dialogs.Confirm(Loc.Get("RobloxSettings.DiscardYourChanges"), Loc.Get("RobloxSettings.ReloadingReadsRobloxsFileAgainAnd"), Loc.Get("RobloxSettings.Reload")))
            return;
        Load();
    }

    // ===== Lifecycle =====

    public override void OnNavigatedTo()
    {
        if (!HasChanges) Load();
        else RefreshStatus();
        OnPropertyChanged(nameof(UseNexstrapForLinks));
        OnPropertyChanged(nameof(LinkHandlerText));
        OnPropertyChanged(nameof(CloseAfterLaunch));
        if (_client.State.Installations.Count == 0) _ = _client.RefreshAsync(checkLatest: false);
    }

    private void RefreshStatus()
    {
        RobloxRunning = _launcher.IsRobloxRunning();
        SettingsFileExists = _robloxSettings.Exists;
    }

    private void Load()
    {
        _loading = true;
        try
        {
            RefreshStatus();
            try
            {
                _loaded = _robloxSettings.Load();
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException or InvalidDataException)
            {
                _loaded = new RobloxUserSettings();
                StatusMessage = Loc.Get("RobloxSettings.RobloxsSettingsFileCouldntBeRead") + ex.Message;
            }

            FramerateChoices.Clear();
            foreach (var fps in StandardFramerates) FramerateChoices.Add(new Choice<int>(fps, $"{fps} FPS"));
            var cap = _loaded.FramerateCap ?? 60;
            var noLimit = Nexstrap.Services.Performance.PerformancePlanner.IsNoLimit(cap);
            if (!noLimit && !StandardFramerates.Contains(cap)) FramerateChoices.Add(new Choice<int>(cap, Loc.Format("Performance.FPSCurrent", cap)));
            FramerateChoices.Add(new Choice<int>(noLimit ? cap : Nexstrap.Services.Performance.PerformancePlanner.NoLimitCap, () => Loc.Get("Performance.NoLimit.Choice")));
            FramerateCap = cap;

            QualityAutomatic = (_loaded.SavedQualityLevel ?? 0) == 0;
            QualityLevel = QualityAutomatic ? 5 : _loaded.SavedQualityLevel!.Value;
            Fullscreen = _loaded.Fullscreen ?? false;
            VolumePercent = Math.Round((_loaded.MasterVolume ?? 1) * 100);
            MouseSensitivity = Math.Round(_loaded.MouseSensitivity ?? 1, 1);
            InvertCamera = _loaded.CameraYInverted ?? false;
            HasChanges = false;
            RefreshBackups();
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in _robloxSettings.ListBackups().Take(6)) Backups.Add(b);
        OnPropertyChanged(nameof(HasBackups));
    }
}
