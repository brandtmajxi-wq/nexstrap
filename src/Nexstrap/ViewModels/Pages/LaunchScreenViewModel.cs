using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Dialogs;

namespace Nexstrap.ViewModels.Pages;

public sealed partial class LaunchScreenViewModel(
    ISettingsService settings,
    ILaunchScreen screen,
    IFileDialogService files,
    IDialogService dialogs) : PageViewModel
{
    public override string Title => Loc.Get("Nav.LoadingScreen");

    public override string Subtitle => Loc.Get("Page.LoadingScreen");

    private LaunchScreenSettings Options => settings.Current.LaunchScreen;

    public IReadOnlyList<LaunchScreenPreset> Presets => LaunchScreenService.Presets;

    public IReadOnlyList<Choice<LaunchAnimation>> AnimationChoices { get; } =
    [
        new(LaunchAnimation.Spinner, () => Loc.Get("LaunchScreen.Spinner")),
        new(LaunchAnimation.Bar, () => Loc.Get("LaunchScreen.SlidingBar")),
        new(LaunchAnimation.Pulse, () => Loc.Get("LaunchScreen.PulsingLogo")),
        new(LaunchAnimation.None, () => Loc.Get("LaunchScreen.None"))
    ];

    public bool Enabled
    {
        get => Options.Enabled;
        set => Change(() => Options.Enabled = value);
    }

    public string ScreenTitle
    {
        get => Options.Title;
        set => Change(() => Options.Title = string.IsNullOrWhiteSpace(value) ? "Starting Roblox" : value.Trim());
    }

    public LaunchAnimation Animation
    {
        get => Options.Animation;
        set => Change(() => Options.Animation = value);
    }

    public string BackgroundColor
    {
        get => Options.BackgroundColor;
        set => SetColor(value, c => Options.BackgroundColor = c);
    }

    public string AccentColor
    {
        get => Options.AccentColor;
        set => SetColor(value, c => Options.AccentColor = c);
    }

    [ObservableProperty]
    private string? _colorError;

    public bool HasImage => Options.BackgroundImage is not null;

    private void SetColor(string value, Action<string> apply)
    {
        if (!ColorUtil.TryParseHex(value, out var color))
        {
            ColorError = Loc.Get("Appearance.UseRGBOrRRGGBBEG");
            return;
        }
        ColorError = null;
        Change(() => apply(ColorUtil.ToHex(color)));
    }

    [RelayCommand]
    private void ApplyPreset(LaunchScreenPreset preset) => Change(() =>
    {
        Options.BackgroundColor = preset.BackgroundColor;
        Options.AccentColor = preset.AccentColor;
        Options.Animation = preset.Animation;
    });

    [RelayCommand]
    private void ChooseImage()
    {
        var filter = "Images|" + string.Join(';', AppearanceService.SupportedImageExtensions.Select(e => "*" + e));
        if (files.OpenFile(Loc.Get("LaunchScreen.ChooseALoadingScreenImage"), filter) is not { } path) return;
        if (screen.SetImage(path) is { } error) dialogs.Show(Loc.Get("Appearance.CouldntUseThatImage"), error, DialogKind.Warning);
        OnPropertyChanged(nameof(HasImage));
    }

    [RelayCommand]
    private void RemoveImage() => Change(() => Options.BackgroundImage = null);

    [RelayCommand]
    private void Preview() => screen.Preview();

    private void Change(Action change)
    {
        change();
        settings.Save();
        OnPropertyChanged(string.Empty);
    }
}
