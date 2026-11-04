using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;
using Nexstrap.Services.Roblox;

namespace Nexstrap.ViewModels.Pages;

/// <summary>A value plus its human label, for combo boxes.</summary>
/// <summary>A dropdown entry. A label given as a function follows the app language.</summary>
public sealed class Choice<T> : ObservableObject
{
    private readonly Func<string> _label;

    public Choice(T value, string label)
    {
        Value = value;
        _label = () => label;
    }

    public Choice(T value, Func<string> label)
    {
        Value = value;
        _label = label;
        Loc.Changed += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public T Value { get; }

    public string Label => _label();
}

public sealed partial class AppearanceViewModel : PageViewModel
{
    private const string ThemeFileFilter = "Nexstrap theme (*.json)|*.json";

    private readonly ISettingsService _settings;
    private readonly IRobloxFontService _robloxFont;
    private readonly IRobloxClientService _client;
    private readonly IAppearanceService _appearance;
    private readonly IThemeService _theme;
    private readonly IFontCatalog _fonts;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly ILogger<AppearanceViewModel> _logger;
    private bool _syncing;
    private bool _fontsRequested;

    public AppearanceViewModel(IAppearanceService appearance, IThemeService theme, IFontCatalog fonts,
        IDialogService dialogs, IFileDialogService files, ISettingsService settings, IRobloxFontService robloxFont,
        IRobloxClientService client, ILogger<AppearanceViewModel> logger)
    {
        _settings = settings;
        _robloxFont = robloxFont;
        _client = client;
        client.StateChanged += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(RobloxFontStatus)));
        _appearance = appearance;
        _theme = theme;
        _fonts = fonts;
        _dialogs = dialogs;
        _files = files;
        _logger = logger;

        FontsView = CollectionViewSource.GetDefaultView(_fontList);
        FontsView.Filter = o => string.IsNullOrWhiteSpace(FontSearch)
                                || ((FontOption)o).Name.Contains(FontSearch.Trim(), StringComparison.OrdinalIgnoreCase);

        _accentHex = appearance.Current.AccentColor;
        appearance.Changed += (_, _) => SyncFromSettings();
        RefreshSavedProfiles();
    }

    public override string Title => Loc.Get("Nav.Appearance");

    public override string Subtitle => Loc.Get("Page.Appearance");

    private AppearanceSettings Current => _appearance.Current;

    // ===== Theme =====

    public bool IsDark
    {
        get => Current.Theme == AppTheme.Dark;
        set { if (value) _appearance.SetTheme(AppTheme.Dark); }
    }

    public bool IsLight
    {
        get => Current.Theme == AppTheme.Light;
        set { if (value) _appearance.SetTheme(AppTheme.Light); }
    }

    // ===== Accent =====

    public IReadOnlyList<string> AccentSwatches { get; } =
        ["#7C6CFF", "#6366F1", "#38BDF8", "#22D3EE", "#14B8A6", "#22C55E", "#EAB308", "#F97316", "#F43F5E", "#EC4899"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccentPreview))]
    private string _accentHex;

    [ObservableProperty]
    private string? _accentError;

    public Brush AccentPreview => ColorUtil.TryParseHex(AccentHex, out var c)
        ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B))
        : Brushes.Transparent;

    partial void OnAccentHexChanged(string value)
    {
        if (_syncing) return;
        if (_appearance.SetAccent(value))
            AccentError = null;
        else
            AccentError = string.IsNullOrWhiteSpace(value) ? Loc.Get("Appearance.EnterAColourLike7C6CFF") : Loc.Get("Appearance.UseRGBOrRRGGBBEG");
    }

    [RelayCommand]
    private void PickSwatch(string hex) => AccentHex = hex;

    // ===== Font =====

    private readonly ObservableCollection<FontOption> _fontList = [];

    public ICollectionView FontsView { get; }

    [ObservableProperty]
    private bool _isLoadingFonts;

    [ObservableProperty]
    private string _fontSearch = "";

    partial void OnFontSearchChanged(string value) => FontsView.Refresh();

    public string CurrentFontName => Current.FontFamily;

    [ObservableProperty]
    private string _previewText = Loc.Get("Appearance.TheQuickBrownFoxJumpsOver");

    private FontOption? _selectedFont;

    public FontOption? SelectedFont
    {
        get => _selectedFont;
        set
        {
            if (!SetProperty(ref _selectedFont, value) || value is null || _syncing) return;
            if (!string.Equals(value.Name, Current.FontFamily, StringComparison.OrdinalIgnoreCase))
                _appearance.SetFont(value.Name);
        }
    }

    [RelayCommand]
    private void ResetFont() => _appearance.SetFont(AppearanceSettings.DefaultFont);

    private async Task LoadFontsAsync()
    {
        IsLoadingFonts = true;
        try
        {
            var fonts = await _fonts.GetFontsAsync();
            _fontList.Clear();
            foreach (var f in fonts) _fontList.Add(f);
            SyncSelectedFont();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Font list failed to load");
        }
        finally
        {
            IsLoadingFonts = false;
        }
    }

    private void SyncSelectedFont()
    {
        _syncing = true;
        SelectedFont = _fontList.FirstOrDefault(f => string.Equals(f.Name, Current.FontFamily, StringComparison.OrdinalIgnoreCase));
        _syncing = false;
    }

    // ===== Language =====

    public IReadOnlyList<LanguageOption> Languages => Loc.Languages;

    public LanguageOption SelectedLanguage
    {
        get => Loc.Languages.First(l => l.Code == Loc.Code);
        set
        {
            if (value is null || value.Code == Loc.Code) return;
            Loc.Apply(value.Code);
            _settings.Current.Language = value.Code;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    // ===== Roblox font =====

    private RobloxFontSettings RobloxFontOptions => _settings.Current.RobloxFont;

    public bool RobloxFontEnabled
    {
        get => RobloxFontOptions.Enabled;
        set
        {
            if (value && RobloxFontOptions.FontFile is null)
            {
                _dialogs.Show(Loc.Get("Appearance.ChooseAFontFirst"), Loc.Get("Appearance.PickAFontFileOrUse"), DialogKind.Info);
            }
            else if (value && !_dialogs.Confirm(Loc.Get("Appearance.ChangeRobloxsFonts"),
                         Loc.Get("Appearance.ThisEditsFontSettingsInsideNexstraps"),
                         Loc.Get("Appearance.TurnOn")))
            {
                // Declined: the toggle goes back below.
            }
            else
            {
                RobloxFontOptions.Enabled = value;
                _settings.Save();
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(RobloxFontStatus));
        }
    }

    public string RobloxFontName => RobloxFontOptions.DisplayName ?? Loc.Get("Appearance.NoneChosen");

    public string RobloxFontStatus
    {
        get
        {
            if (!RobloxFontOptions.Enabled) return Loc.Get("Appearance.OffRobloxUsesItsOwnFonts");
            return _client.State.Preferred?.Source == InstallationSource.Nexstrap
                ? Loc.Get("Appearance.OnAppliedEachTimeYouStart")
                : Loc.Get("Appearance.OnButPlayIsntUsingA");
        }
    }

    [RelayCommand]
    private void ChooseRobloxFontFile()
    {
        if (_files.OpenFile(Loc.Get("Appearance.ChooseAFontForRoblox"), "Fonts (*.ttf;*.otf)|*.ttf;*.otf") is not { } path) return;
        ShowRobloxFontResult(_robloxFont.SetFontFile(path));
    }

    [RelayCommand]
    private void UseNexstrapFontForRoblox() => ShowRobloxFontResult(_robloxFont.SetInstalledFont(Current.FontFamily));

    private void ShowRobloxFontResult(string? error)
    {
        if (error is not null) _dialogs.Show(Loc.Get("Appearance.CouldntUseThatFont"), error, DialogKind.Warning);
        OnPropertyChanged(nameof(RobloxFontName));
    }

    // ===== Background =====

    public IReadOnlyList<Choice<BackgroundFit>> FitChoices { get; } =
    [
        new(BackgroundFit.Fill, () => Loc.Get("Appearance.FillCropToCover")),
        new(BackgroundFit.Fit, () => Loc.Get("Appearance.FitShowWholeImage")),
        new(BackgroundFit.Stretch, () => Loc.Get("Appearance.Stretch")),
        new(BackgroundFit.Center, () => Loc.Get("Appearance.CenterOriginalSize")),
        new(BackgroundFit.Tile, () => Loc.Get("Appearance.Tile"))
    ];

    public IReadOnlyList<Choice<BackgroundAnchor>> AnchorChoices { get; } =
    [
        new(BackgroundAnchor.Center, () => Loc.Get("Appearance.Center")),
        new(BackgroundAnchor.Top, () => Loc.Get("Appearance.Top")),
        new(BackgroundAnchor.Bottom, () => Loc.Get("Appearance.Bottom")),
        new(BackgroundAnchor.Left, () => Loc.Get("Appearance.Left")),
        new(BackgroundAnchor.Right, () => Loc.Get("Appearance.Right"))
    ];

    public bool HasBackground => Current.BackgroundImage is not null;

    public ImageSource? BackgroundPreview => Current.BackgroundImage is { } path ? _theme.LoadImage(path) : null;

    public double BackgroundOpacityPercent
    {
        get => Math.Round(Current.BackgroundOpacity * 100);
        set => _appearance.SetBackgroundOpacity(value / 100.0);
    }

    public BackgroundFit SelectedFit
    {
        get => Current.BackgroundFit;
        set { if (!_syncing && value != Current.BackgroundFit) _appearance.SetBackgroundFit(value); }
    }

    public BackgroundAnchor SelectedAnchor
    {
        get => Current.BackgroundAnchor;
        set { if (!_syncing && value != Current.BackgroundAnchor) _appearance.SetBackgroundAnchor(value); }
    }

    [RelayCommand]
    private void ChooseBackground()
    {
        var filter = "Images|" + string.Join(';', AppearanceService.SupportedImageExtensions.Select(e => "*" + e));
        var path = _files.OpenFile(Loc.Get("Appearance.ChooseABackgroundImage"), filter);
        if (path is null) return;

        if (_appearance.SetBackgroundFromFile(path) is { } error)
            _dialogs.Show(Loc.Get("Appearance.CouldntUseThatImage"), error, DialogKind.Warning);
    }

    [RelayCommand]
    private void RemoveBackground() => _appearance.ClearBackground();

    // ===== Presets & saved themes =====

    public IReadOnlyList<ThemeProfile> Presets => _appearance.Presets;

    public ObservableCollection<ThemeProfile> SavedProfiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _newProfileName = "";

    [RelayCommand]
    private void ApplyPreset(ThemeProfile preset) => _appearance.ApplyProfile(preset);

    private bool CanSaveProfile() => !string.IsNullOrWhiteSpace(NewProfileName);

    [RelayCommand(CanExecute = nameof(CanSaveProfile))]
    private void SaveProfile()
    {
        var name = NewProfileName.Trim();
        if (_appearance.ProfileExists(name) &&
            !_dialogs.Confirm(Loc.Get("Appearance.ReplaceTheme"), Loc.Format("Appearance.AThemeCalledAlreadyExistsReplace", name), Loc.Get("Appearance.Replace")))
            return;

        try
        {
            _appearance.SaveProfile(name);
            NewProfileName = "";
            RefreshSavedProfiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("Appearance.CouldntSaveTheme"), ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void ApplySaved(ThemeProfile profile) => _appearance.ApplyProfile(profile);

    [RelayCommand]
    private void DeleteSaved(ThemeProfile profile)
    {
        if (!_dialogs.Confirm(Loc.Get("Appearance.DeleteTheme"), Loc.Format("Appearance.WillBeRemovedYourCurrentLook", profile.Name), Loc.Get("Appearance.Delete")))
            return;
        try
        {
            _appearance.DeleteProfile(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("Appearance.CouldntDeleteTheme"), ex.Message, DialogKind.Error);
        }
        RefreshSavedProfiles();
    }

    [RelayCommand]
    private void ExportSaved(ThemeProfile profile)
    {
        var path = _files.SaveFile(Loc.Get("Appearance.ExportTheme"), ThemeFileFilter, profile.Name + ".json");
        if (path is null) return;
        try
        {
            _appearance.ExportProfile(profile, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("Appearance.CouldntExportTheme"), ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void ImportProfile()
    {
        var path = _files.OpenFile(Loc.Get("Appearance.ImportTheme2"), ThemeFileFilter);
        if (path is null) return;
        try
        {
            var profile = _appearance.ImportProfile(path);
            RefreshSavedProfiles();
            if (_dialogs.Confirm(Loc.Get("Appearance.ThemeImported"), Loc.Format("Appearance.WasAddedToYourThemesApply", profile.Name), Loc.Get("Appearance.Apply"), Loc.Get("Appearance.Later")))
                _appearance.ApplyProfile(profile);
        }
        catch (InvalidDataException ex)
        {
            _dialogs.Show(Loc.Get("Appearance.CouldntImportTheme"), ex.Message, DialogKind.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show(Loc.Get("Appearance.CouldntImportTheme"), ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void ResetAppearance()
    {
        if (_dialogs.Confirm(Loc.Get("Appearance.ResetAppearance2"),
                Loc.Get("Appearance.ThemeAccentColourFontAndBackground"), Loc.Get("Appearance.Reset")))
            _appearance.Reset();
    }

    public bool HasSavedProfiles => SavedProfiles.Count > 0;

    private void RefreshSavedProfiles()
    {
        SavedProfiles.Clear();
        foreach (var p in _appearance.LoadSavedProfiles()) SavedProfiles.Add(p);
        OnPropertyChanged(nameof(HasSavedProfiles));
    }

    // ===== Lifecycle =====

    public override void OnNavigatedTo()
    {
        if (_fontsRequested) return;
        _fontsRequested = true;
        _ = LoadFontsAsync();
    }

    /// <summary>Re-reads every bound value after a change (from this page, Home or a profile).</summary>
    private void SyncFromSettings()
    {
        _syncing = true;
        try
        {
            if (!string.Equals(AccentHex, Current.AccentColor, StringComparison.OrdinalIgnoreCase)
                && !(ColorUtil.TryParseHex(AccentHex, out var typed) && ColorUtil.ToHex(typed) == Current.AccentColor))
            {
                AccentHex = Current.AccentColor;
                AccentError = null;
            }
            OnPropertyChanged(nameof(IsDark));
            OnPropertyChanged(nameof(IsLight));
            OnPropertyChanged(nameof(CurrentFontName));
            OnPropertyChanged(nameof(HasBackground));
            OnPropertyChanged(nameof(BackgroundPreview));
            OnPropertyChanged(nameof(BackgroundOpacityPercent));
            OnPropertyChanged(nameof(SelectedFit));
            OnPropertyChanged(nameof(SelectedAnchor));
        }
        finally
        {
            _syncing = false;
        }
        SyncSelectedFont();
    }
}
