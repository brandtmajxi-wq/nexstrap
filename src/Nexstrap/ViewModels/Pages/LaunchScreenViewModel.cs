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
        new(LaunchAnimation.Dots, () => Loc.Get("LaunchScreen.BouncingDots")),
        new(LaunchAnimation.None, () => Loc.Get("LaunchScreen.None"))
    ];

    public IReadOnlyList<Choice<LaunchSpeed>> SpeedChoices { get; } =
    [
        new(LaunchSpeed.Slow, () => Loc.Get("LaunchScreen.Slow")),
        new(LaunchSpeed.Normal, () => Loc.Get("LaunchScreen.Normal")),
        new(LaunchSpeed.Fast, () => Loc.Get("LaunchScreen.Fast"))
    ];

    public IReadOnlyList<Choice<LaunchTextSize>> TextSizeChoices { get; } =
    [
        new(LaunchTextSize.Small, () => Loc.Get("LaunchScreen.Small")),
        new(LaunchTextSize.Medium, () => Loc.Get("LaunchScreen.Medium")),
        new(LaunchTextSize.Large, () => Loc.Get("LaunchScreen.Large"))
    ];

    public IReadOnlyList<Choice<LaunchWindowSize>> WindowSizeChoices { get; } =
    [
        new(LaunchWindowSize.Small, () => Loc.Get("LaunchScreen.Small")),
        new(LaunchWindowSize.Medium, () => Loc.Get("LaunchScreen.Medium")),
        new(LaunchWindowSize.Large, () => Loc.Get("LaunchScreen.Large"))
    ];

    public IReadOnlyList<Choice<LaunchPosition>> PositionChoices { get; } =
    [
        new(LaunchPosition.Center, () => Loc.Get("LaunchScreen.Center")),
        new(LaunchPosition.TopLeft, () => Loc.Get("LaunchScreen.TopLeft")),
        new(LaunchPosition.TopRight, () => Loc.Get("LaunchScreen.TopRight")),
        new(LaunchPosition.BottomLeft, () => Loc.Get("LaunchScreen.BottomLeft")),
        new(LaunchPosition.BottomRight, () => Loc.Get("LaunchScreen.BottomRight"))
    ];

    public IReadOnlyList<Choice<LaunchCorners>> CornerChoices { get; } =
    [
        new(LaunchCorners.Square, () => Loc.Get("LaunchScreen.Square")),
        new(LaunchCorners.Rounded, () => Loc.Get("LaunchScreen.Rounded")),
        new(LaunchCorners.VeryRounded, () => Loc.Get("LaunchScreen.VeryRounded"))
    ];

    public IReadOnlyList<Choice<LaunchLogo>> LogoChoices { get; } =
    [
        new(LaunchLogo.Nexstrap, "Nexstrap"),
        new(LaunchLogo.Custom, () => Loc.Get("LaunchScreen.CustomImage")),
        new(LaunchLogo.Hidden, () => Loc.Get("LaunchScreen.Hidden"))
    ];

    public IReadOnlyList<Choice<LaunchImageFit>> ImageFitChoices { get; } =
    [
        new(LaunchImageFit.Fill, () => Loc.Get("LaunchScreen.FillWindow")),
        new(LaunchImageFit.Fit, () => Loc.Get("LaunchScreen.ShowWholeImage"))
    ];

    public IReadOnlyList<Choice<int>> MinimumChoices { get; } =
    [
        new(0, () => Loc.Get("LaunchScreen.None")),
        new(2, () => Loc.Format("LaunchScreen.Seconds", 2)),
        new(5, () => Loc.Format("LaunchScreen.Seconds", 5))
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

    public string ScreenSubtitle
    {
        get => Options.Subtitle ?? "";
        set => Change(() => Options.Subtitle = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public LaunchTextSize TextSize
    {
        get => Options.TextSize;
        set => Change(() => Options.TextSize = value);
    }

    public LaunchAnimation Animation
    {
        get => Options.Animation;
        set => Change(() => Options.Animation = value);
    }

    public LaunchSpeed Speed
    {
        get => Options.Speed;
        set => Change(() => Options.Speed = value);
    }

    public LaunchWindowSize WindowSize
    {
        get => Options.WindowSize;
        set => Change(() => Options.WindowSize = value);
    }

    public LaunchPosition Position
    {
        get => Options.Position;
        set => Change(() => Options.Position = value);
    }

    public LaunchCorners Corners
    {
        get => Options.Corners;
        set => Change(() => Options.Corners = value);
    }

    public bool Fade
    {
        get => Options.Fade;
        set => Change(() => Options.Fade = value);
    }

    public int MinimumSeconds
    {
        get => Options.MinimumSeconds;
        set => Change(() => Options.MinimumSeconds = value);
    }

    public LaunchLogo Logo
    {
        get => Options.Logo;
        set => Change(() => Options.Logo = value);
    }

    public bool ChoosesLogo => Options.Logo == LaunchLogo.Custom;

    public LaunchImageFit ImageFit
    {
        get => Options.ImageFit;
        set => Change(() => Options.ImageFit = value);
    }

    public double ImageOpacity
    {
        get => Options.ImageOpacity;
        set => Change(() => Options.ImageOpacity = Math.Clamp(value, 0.1, 1));
    }

    public string ImageOpacityLabel => $"{Math.Round(Options.ImageOpacity * 100)}%";

    public bool ImageBlur
    {
        get => Options.ImageBlur;
        set => Change(() => Options.ImageBlur = value);
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

    /// <summary>Empty means black or white, whichever is easier to read.</summary>
    public string TextColor
    {
        get => Options.TextColor ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ColorError = null;
                Change(() => Options.TextColor = null);
                return;
            }
            SetColor(value, c => Options.TextColor = c);
        }
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

    private static string ImageFilter => "Images|" + string.Join(';', AppearanceService.SupportedImageExtensions.Select(e => "*" + e));

    [RelayCommand]
    private void ChooseImage()
    {
        if (files.OpenFile(Loc.Get("LaunchScreen.ChooseALoadingScreenImage"), ImageFilter) is not { } path) return;
        if (screen.SetImage(path) is { } error) dialogs.Show(Loc.Get("Appearance.CouldntUseThatImage"), error, DialogKind.Warning);
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void RemoveImage() => Change(() => Options.BackgroundImage = null);

    [RelayCommand]
    private void ChooseLogo()
    {
        if (files.OpenFile(Loc.Get("LaunchScreen.ChooseALogo"), ImageFilter) is not { } path) return;
        if (screen.SetLogo(path) is { } error) dialogs.Show(Loc.Get("Appearance.CouldntUseThatImage"), error, DialogKind.Warning);
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void Preview() => screen.Preview();

    [RelayCommand]
    private void Reset()
    {
        if (!dialogs.Confirm(Loc.Get("LaunchScreen.Reset"), Loc.Get("LaunchScreen.Reset.Text"), Loc.Get("LaunchScreen.Reset"))) return;
        ColorError = null;
        Change(() => settings.Current.LaunchScreen = new LaunchScreenSettings { Enabled = Options.Enabled });
    }

    private void Change(Action change)
    {
        change();
        settings.Save();
        OnPropertyChanged(string.Empty);
    }
}
