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
using Nexstrap.Services.Dialogs;

namespace Nexstrap.ViewModels.Pages;

/// <summary>A value plus its human label, for combo boxes.</summary>
public sealed record Choice<T>(T Value, string Label);

public sealed partial class AppearanceViewModel : PageViewModel
{
    private const string ThemeFileFilter = "Nexstrap theme (*.json)|*.json";

    private readonly IAppearanceService _appearance;
    private readonly IThemeService _theme;
    private readonly IFontCatalog _fonts;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly ILogger<AppearanceViewModel> _logger;
    private bool _syncing;
    private bool _fontsRequested;

    public AppearanceViewModel(IAppearanceService appearance, IThemeService theme, IFontCatalog fonts,
        IDialogService dialogs, IFileDialogService files, ILogger<AppearanceViewModel> logger)
    {
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

    public override string Title => "Appearance";

    public override string Subtitle => "Every change applies instantly and is saved automatically.";

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
            AccentError = string.IsNullOrWhiteSpace(value) ? "Enter a colour like #7C6CFF." : "Use #RGB or #RRGGBB, e.g. #7C6CFF.";
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
    private string _previewText = "The quick brown fox jumps over the lazy dog · 0123456789";

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

    // ===== Background =====

    public IReadOnlyList<Choice<BackgroundFit>> FitChoices { get; } =
    [
        new(BackgroundFit.Fill, "Fill (crop to cover)"),
        new(BackgroundFit.Fit, "Fit (show whole image)"),
        new(BackgroundFit.Stretch, "Stretch"),
        new(BackgroundFit.Center, "Center (original size)"),
        new(BackgroundFit.Tile, "Tile")
    ];

    public IReadOnlyList<Choice<BackgroundAnchor>> AnchorChoices { get; } =
    [
        new(BackgroundAnchor.Center, "Center"),
        new(BackgroundAnchor.Top, "Top"),
        new(BackgroundAnchor.Bottom, "Bottom"),
        new(BackgroundAnchor.Left, "Left"),
        new(BackgroundAnchor.Right, "Right")
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
        var path = _files.OpenFile("Choose a background image", filter);
        if (path is null) return;

        if (_appearance.SetBackgroundFromFile(path) is { } error)
            _dialogs.Show("Couldn't use that image", error, DialogKind.Warning);
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
            !_dialogs.Confirm("Replace theme?", $"A theme called \"{name}\" already exists. Replace it with the current look?", "Replace"))
            return;

        try
        {
            _appearance.SaveProfile(name);
            NewProfileName = "";
            RefreshSavedProfiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show("Couldn't save theme", ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void ApplySaved(ThemeProfile profile) => _appearance.ApplyProfile(profile);

    [RelayCommand]
    private void DeleteSaved(ThemeProfile profile)
    {
        if (!_dialogs.Confirm("Delete theme?", $"\"{profile.Name}\" will be removed. Your current look won't change.", "Delete"))
            return;
        try
        {
            _appearance.DeleteProfile(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show("Couldn't delete theme", ex.Message, DialogKind.Error);
        }
        RefreshSavedProfiles();
    }

    [RelayCommand]
    private void ExportSaved(ThemeProfile profile)
    {
        var path = _files.SaveFile("Export theme", ThemeFileFilter, profile.Name + ".json");
        if (path is null) return;
        try
        {
            _appearance.ExportProfile(profile, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show("Couldn't export theme", ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void ImportProfile()
    {
        var path = _files.OpenFile("Import theme", ThemeFileFilter);
        if (path is null) return;
        try
        {
            var profile = _appearance.ImportProfile(path);
            RefreshSavedProfiles();
            if (_dialogs.Confirm("Theme imported", $"\"{profile.Name}\" was added to your themes. Apply it now?", "Apply", "Later"))
                _appearance.ApplyProfile(profile);
        }
        catch (InvalidDataException ex)
        {
            _dialogs.Show("Couldn't import theme", ex.Message, DialogKind.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Show("Couldn't import theme", ex.Message, DialogKind.Error);
        }
    }

    [RelayCommand]
    private void ResetAppearance()
    {
        if (_dialogs.Confirm("Reset appearance?",
                "Theme, accent colour, font and background go back to the Nexstrap defaults. Your saved themes are kept.", "Reset"))
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
