using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Discord;
using Nexstrap.Services.Integrations;

namespace Nexstrap.ViewModels.Pages;

/// <summary>Editable row for a companion app; changes save immediately.</summary>
public sealed partial class CompanionRow(CompanionApp app, ICompanionAppService service) : ObservableObject
{
    public CompanionApp App { get; } = app;

    public string Name => App.Name;

    public string Path => App.Path;

    public bool Exists => System.IO.File.Exists(App.Path);

    public bool Enabled
    {
        get => App.Enabled;
        set { App.Enabled = value; service.Update(App); OnPropertyChanged(); }
    }

    public bool CloseWithRoblox
    {
        get => App.CloseWithRoblox;
        set { App.CloseWithRoblox = value; service.Update(App); OnPropertyChanged(); }
    }

    public string Arguments
    {
        get => App.Arguments;
        set { App.Arguments = value.Trim(); service.Update(App); OnPropertyChanged(); }
    }
}

public sealed partial class IntegrationsViewModel : PageViewModel
{
    private readonly IRichPresenceService _presence;
    private readonly ISettingsService _settings;
    private readonly ICompanionAppService _companions;
    private readonly IConfigTransferService _transfer;
    private readonly IFileDialogService _files;
    private readonly IDialogService _dialogs;
    private readonly ICardImageUploader _uploader;

    public IntegrationsViewModel(IRichPresenceService presence, ISettingsService settings, ICompanionAppService companions,
        IConfigTransferService transfer, IFileDialogService files, IDialogService dialogs, ICardImageUploader uploader)
    {
        _uploader = uploader;
        _presence = presence;
        _settings = settings;
        _companions = companions;
        _transfer = transfer;
        _files = files;
        _dialogs = dialogs;
        _applicationId = settings.Current.Discord.ApplicationIdOverride ?? "";
        presence.StatusChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(OnStatusChanged);
        companions.Changed += (_, _) => RebuildCompanions();
        RebuildCompanions();
    }

    // ===== Companion apps =====

    public ObservableCollection<CompanionRow> Companions { get; } = [];

    public bool HasCompanions => Companions.Count > 0;

    [RelayCommand]
    private void AddCompanion()
    {
        var path = _files.OpenFile(Loc.Get("Integrations.ChooseAProgramToStartWith"), "Programs (*.exe)|*.exe");
        if (path is null) return;
        if (_companions.Add(path) is { } error)
            _dialogs.Show(Loc.Get("Integrations.CouldntAddThatProgram"), error, DialogKind.Warning);
    }

    [RelayCommand]
    private void RemoveCompanion(CompanionRow row) => _companions.Remove(row.App);

    [RelayCommand]
    private void TestCompanion(CompanionRow row)
    {
        if (_companions.TestLaunch(row.App) is { } error)
            _dialogs.Show(Loc.Get("Integrations.CouldntStartIt"), error, DialogKind.Warning);
    }

    private void RebuildCompanions()
    {
        Companions.Clear();
        foreach (var app in _companions.Apps) Companions.Add(new CompanionRow(app, _companions));
        OnPropertyChanged(nameof(HasCompanions));
    }

    // ===== Configuration transfer =====

    [ObservableProperty]
    private string? _transferMessage;

    [RelayCommand]
    private void ExportConfig()
    {
        var path = _files.SaveFile(Loc.Get("Integrations.ExportNexstrapConfiguration"), "Nexstrap configuration (*.json)|*.json",
            $"Nexstrap-config-{DateTime.Now:yyyy-MM-dd}.json");
        if (path is null) return;
        try
        {
            _transfer.Export(path);
            TransferMessage = Loc.Format("Integrations.ExportedTo", System.IO.Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("FastFlags.CouldntExport"), ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void ImportConfig()
    {
        var path = _files.OpenFile(Loc.Get("Integrations.ImportNexstrapConfiguration"), "Nexstrap configuration (*.json)|*.json");
        if (path is null) return;
        if (!_dialogs.Confirm(Loc.Get("Integrations.ImportThisConfiguration"),
                Loc.Get("Integrations.YourAppearanceLaunchAndDiscordSettings"), Loc.Get("Integrations.Import2")))
            return;
        try
        {
            var result = _transfer.Import(path);
            ApplicationId = _settings.Current.Discord.ApplicationIdOverride ?? "";
            OnPropertyChanged(string.Empty);
            RebuildCompanions();
            _ = _presence.RefreshAsync();
            TransferMessage = Loc.Format("Integrations.ImportedFastFlagsThemesAndCompanionApps", result.FastFlags, result.Themes, result.CompanionApps);
            if (result.Notes.Count > 0)
                _dialogs.Show(Loc.Get("Integrations.ImportedWithNotes"), Loc.Get("Integrations.SomePartsCouldntBeUsedOn"), DialogKind.Info, string.Join(Environment.NewLine, result.Notes));
        }
        catch (System.IO.InvalidDataException ex)
        {
            _dialogs.Show(Loc.Get("FastFlags.CouldntImport"), ex.Message, DialogKind.Warning);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("FastFlags.CouldntImport"), ex.Message, DialogKind.Error);
        }
    }

    public override string Title => Loc.Get("Nav.Integrations");

    public override string Subtitle => Loc.Get("Page.Integrations");

    // ===== Discord Rich Presence =====

    public string StatusText => _presence.StatusText;

    public PresenceStatus Status => _presence.Status;

    public bool IsPreviewing => _presence.IsPreviewing;

    public string PreviewButtonText => IsPreviewing ? Loc.Get("Integrations.StopPreview") : Loc.Get("Integrations.PreviewOnDiscord");

    public bool RichPresence
    {
        get => _settings.Current.Discord.RichPresence;
        set => Update(() => _settings.Current.Discord.RichPresence = value);
    }

    public bool ShowExperienceDetails
    {
        get => _settings.Current.Discord.ShowExperienceDetails;
        set => Update(() => _settings.Current.Discord.ShowExperienceDetails = value);
    }

    /// <summary>Empty falls back to the default text, both here and on Discord.</summary>
    public string MenuTopLine
    {
        get => _settings.Current.Discord.MenuTopLine;
        set => Update(() => _settings.Current.Discord.MenuTopLine =
            string.IsNullOrWhiteSpace(value) ? DiscordSettings.DefaultMenuTopLine : value.Trim());
    }

    public string MenuBottomLine
    {
        get => _settings.Current.Discord.MenuBottomLine;
        set => Update(() => _settings.Current.Discord.MenuBottomLine =
            string.IsNullOrWhiteSpace(value) ? DiscordSettings.DefaultMenuBottomLine : value.Trim());
    }

    public string MenuImage
    {
        get => _settings.Current.Discord.MenuImage ?? "";
        set
        {
            var text = value.Trim();
            if (text.Length > 0 && !RichPresenceService.IsValidImage(text))
            {
                MenuImageError = Loc.Get("Integrations.UseADirectHttpsImageLink");
                return;
            }
            MenuImageError = null;
            Update(() => _settings.Current.Discord.MenuImage = text.Length == 0 ? null : text);
        }
    }

    [ObservableProperty]
    private string? _menuImageError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadImageText))]
    private bool _isUploadingImage;

    public string UploadImageText => Loc.Get(IsUploadingImage ? "Integrations.CardIcon.Uploading" : "Integrations.CardIcon.Upload");

    [RelayCommand]
    private async Task UploadMenuImageAsync()
    {
        var path = _files.OpenFile(Loc.Get("Integrations.CardIcon.Choose"), "Images (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif");
        if (path is null) return;
        if (!_dialogs.Confirm(Loc.Get("Integrations.CardIcon.ConfirmTitle"), Loc.Get("Integrations.CardIcon.ConfirmText"), Loc.Get("Integrations.CardIcon.ConfirmButton")))
            return;

        IsUploadingImage = true;
        try
        {
            MenuImage = await _uploader.UploadAsync(path);
            OnPropertyChanged(nameof(MenuImage));
        }
        catch (System.IO.InvalidDataException ex)
        {
            _dialogs.Show(Loc.Get("Integrations.CardIcon.Failed"), ex.Message, DialogKind.Warning);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.IO.IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("Integrations.CardIcon.Failed"), Loc.Get("Integrations.CardIcon.NoConnection"), DialogKind.Warning, ex.Message);
        }
        finally
        {
            IsUploadingImage = false;
        }
    }

    public bool ShowAvatar
    {
        get => _settings.Current.Discord.ShowAvatar;
        set => Update(() => _settings.Current.Discord.ShowAvatar = value);
    }

    public bool ShowServerLocation
    {
        get => _settings.Current.Discord.ShowServerLocation;
        set => Update(() => _settings.Current.Discord.ShowServerLocation = value);
    }

    public bool ShowNexstrapButton
    {
        get => _settings.Current.Discord.ShowNexstrapButton;
        set => Update(() => _settings.Current.Discord.ShowNexstrapButton = value);
    }

    public bool ShowExperienceButton
    {
        get => _settings.Current.Discord.ShowExperienceButton;
        set => Update(() => _settings.Current.Discord.ShowExperienceButton = value);
    }

    [ObservableProperty]
    private string _applicationId;

    [ObservableProperty]
    private string? _applicationIdError;

    partial void OnApplicationIdChanged(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 0 && !RichPresenceService.IsValidApplicationId(trimmed))
        {
            ApplicationIdError = Loc.Get("Integrations.AnApplicationIDIs1720");
            return;
        }
        ApplicationIdError = null;
        Update(() => _settings.Current.Discord.ApplicationIdOverride = trimmed.Length == 0 ? null : trimmed);
    }

    [RelayCommand]
    private Task TogglePreviewAsync() => _presence.SetPreviewAsync(!_presence.IsPreviewing);

    public override void OnNavigatedFrom()
    {
        // A forgotten preview shouldn't linger on the user's profile.
        if (_presence.IsPreviewing) _ = _presence.SetPreviewAsync(false);
    }

    private void Update(Action change)
    {
        change();
        _settings.Save();
        OnPropertyChanged(string.Empty);
        _ = _presence.RefreshAsync();
    }

    private void OnStatusChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsPreviewing));
        OnPropertyChanged(nameof(PreviewButtonText));
    }
}
