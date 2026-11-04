using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Roblox;
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
    private RobloxUserSettings _loaded = new();
    private bool _loading;

    public RobloxSettingsViewModel(IRobloxUserSettingsService robloxSettings, IRobloxLauncher launcher, IRobloxClientService client,
        IProtocolRegistration protocol, ISettingsService settings, IDialogService dialogs, IShellService shell, INavigationService navigation)
    {
        _robloxSettings = robloxSettings;
        _launcher = launcher;
        _client = client;
        _protocol = protocol;
        _settings = settings;
        _dialogs = dialogs;
        _shell = shell;
        _navigation = navigation;
        client.StateChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(InstallationText)));
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
        !SettingsFileExists ? "Roblox hasn't created its settings file yet. Start Roblox once, then come back."
        : RobloxRunning ? "Roblox is open. Close it to change these settings, because Roblox saves its own copy when it exits."
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

    public string QualityLabel => QualityAutomatic ? "Automatic" : $"{QualityLevel:0} / 10";

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
            _dialogs.Show("Can't save right now", EditBlockedReason!, DialogKind.Warning);
            return;
        }

        try
        {
            _robloxSettings.Save(BuildChanges());
            Load();
            StatusMessage = "Saved. Roblox uses these the next time it starts. A backup of the old file was made.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            _dialogs.Show("Couldn't save Roblox's settings", ex.Message, DialogKind.Error);
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
        StatusMessage = "Changes discarded.";
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

    public bool UseNexstrapForLinks
    {
        get => _protocol.IsRegistered;
        set
        {
            try
            {
                if (value) _protocol.Register();
                else _protocol.Unregister();
                _ = _client.RefreshAsync(checkLatest: false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                _dialogs.Show("Couldn't change the link handler", ex.Message, DialogKind.Error);
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkHandlerText));
        }
    }

    public string LinkHandlerText => _protocol.IsRegistered
        ? "roblox.com Play buttons open Nexstrap. Turning this off gives them back to the previous launcher. If you move Nexstrap.exe, turn this off and on again."
        : $"Currently handled by {_client.State.ProtocolHandler.OwnerName}. Turning this on makes the website's Play buttons go through Nexstrap (your Discord presence and FastFlags then apply to them too).";

    // ===== Installation =====

    public string InstallationText => _client.State.Preferred is { } p
        ? $"Play uses Roblox {p.DisplayVersion} ({p.SourceLabel})."
        : "No Roblox build found.";

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
            StatusMessage = $"Backed up Roblox's settings ({b.CreatedLocal:g}).";
            RefreshBackups();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _dialogs.Show("Couldn't back up", ex.Message, DialogKind.Warning);
        }
    }

    [RelayCommand]
    private void Restore(SettingsBackup backup)
    {
        if (!_dialogs.Confirm("Restore Roblox's settings?",
                $"Roblox's settings will go back to how they were on {backup.CreatedLocal:g}. The current file is backed up first.", "Restore"))
            return;
        try
        {
            _robloxSettings.Restore(backup);
            Load();
            StatusMessage = $"Restored the backup from {backup.CreatedLocal:g}.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            _dialogs.Show("Couldn't restore", ex.Message, DialogKind.Error);
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
        if (HasChanges && !_dialogs.Confirm("Discard your changes?", "Reloading reads Roblox's file again and drops unsaved changes.", "Reload"))
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
                StatusMessage = "Roblox's settings file couldn't be read: " + ex.Message;
            }

            FramerateChoices.Clear();
            foreach (var fps in StandardFramerates) FramerateChoices.Add(new Choice<int>(fps, $"{fps} FPS"));
            var cap = _loaded.FramerateCap ?? 60;
            if (!StandardFramerates.Contains(cap)) FramerateChoices.Add(new Choice<int>(cap, $"{cap} FPS (current)"));
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
