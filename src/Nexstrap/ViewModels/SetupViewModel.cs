using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Setup;

namespace Nexstrap.ViewModels;

public enum SetupStep { Welcome, Options, Done }

public enum SetupOutcome
{
    /// <summary>Window closed; nothing else should open.</summary>
    Closed,
    /// <summary>Carry on in this copy without installing.</summary>
    RunPortable,
    /// <summary>The installed copy should be started.</summary>
    OpenInstalled
}

/// <summary>Language, options and progress for the setup window.</summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly IInstallService _install;
    private readonly ISettingsService _settings;
    private readonly IFileDialogService _files;
    private readonly ILogger<SetupViewModel> _logger;

    public SetupViewModel(IInstallService install, ISettingsService settings, IFileDialogService files, ILogger<SetupViewModel> logger)
    {
        _install = install;
        _settings = settings;
        _files = files;
        _logger = logger;

        var installed = install.InstalledFolder;
        IsUpdate = installed is not null;
        _folder = installed ?? install.DefaultFolder;
        _desktopShortcut = !IsUpdate || install.HasDesktopShortcut;
        _startMenuShortcut = !IsUpdate || install.HasStartMenuShortcut;
        _selectedLanguage = Loc.Languages.First(l => l.Code == Loc.Code);
    }

    public bool IsUpdate { get; }

    public IReadOnlyList<LanguageOption> Languages => Loc.Languages;

    public string InstalledExe { get; private set; } = "";

    public SetupOutcome Outcome { get; private set; } = SetupOutcome.Closed;

    /// <summary>Raised when the window should close.</summary>
    public event EventHandler? Finished;

    public string WelcomeHeading => Loc.Get(IsUpdate ? "Setup.Update.Heading" : "Setup.Welcome.Heading");

    public string WelcomeText => string.Format(Loc.Get(IsUpdate ? "Setup.Update.Text" : "Setup.Welcome.Text"), AppInfo.VersionString);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsOptions), nameof(IsDone))]
    private SetupStep _step;

    public bool IsWelcome => Step == SetupStep.Welcome;

    public bool IsOptions => Step == SetupStep.Options;

    public bool IsDone => Step == SetupStep.Done;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        Loc.Apply(value.Code);
        _settings.Current.Language = value.Code;
        _settings.Save();
        OnPropertyChanged(nameof(WelcomeHeading));
        OnPropertyChanged(nameof(WelcomeText));
    }

    [ObservableProperty]
    private string _folder;

    [ObservableProperty]
    private bool _desktopShortcut;

    [ObservableProperty]
    private bool _startMenuShortcut;

    [ObservableProperty]
    private bool _handleLinks = true;

    [ObservableProperty]
    private bool _openAfterwards = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(BackCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    [RelayCommand]
    private void Next() => Step = SetupStep.Options;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Back()
    {
        Error = null;
        Step = SetupStep.Welcome;
    }

    private bool IsIdle => !IsBusy;

    [RelayCommand]
    private void Browse()
    {
        if (_files.PickFolder(Loc.Get("Setup.Location")) is not { } picked) return;
        // Never spill into a shared folder like Desktop.
        Folder = string.Equals(Path.GetFileName(picked.TrimEnd('\\')), AppInfo.Name, StringComparison.OrdinalIgnoreCase)
            ? picked
            : Path.Combine(picked, AppInfo.Name);
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task InstallAsync()
    {
        Error = null;
        if (_install.ValidateFolder(Folder) is { } problem)
        {
            Error = Loc.Get(problem);
            return;
        }

        IsBusy = true;
        try
        {
            var options = new InstallOptions(Folder, DesktopShortcut, StartMenuShortcut, HandleLinks);
            InstalledExe = await Task.Run(() => _install.Install(options));
            Step = SetupStep.Done;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogError(ex, "Install failed");
            Error = string.Format(Loc.Get("Setup.Error.Failed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Finish()
    {
        Outcome = OpenAfterwards ? SetupOutcome.OpenInstalled : SetupOutcome.Closed;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RunPortable()
    {
        _settings.Current.SkippedSetup = true;
        _settings.Save();
        Outcome = SetupOutcome.RunPortable;
        Finished?.Invoke(this, EventArgs.Empty);
    }
}
