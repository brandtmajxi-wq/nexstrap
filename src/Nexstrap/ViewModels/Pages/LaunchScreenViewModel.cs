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
    public override string Title => "Loading Screen";

    public override string Subtitle => "What you see while Roblox starts. It closes by itself when Roblox opens.";

    private LaunchScreenSettings Options => settings.Current.LaunchScreen;

    public IReadOnlyList<LaunchScreenPreset> Presets => LaunchScreenService.Presets;

    public IReadOnlyList<Choice<LaunchAnimation>> AnimationChoices { get; } =
    [
        new(LaunchAnimation.Spinner, "Spinner"),
        new(LaunchAnimation.Bar, "Sliding bar"),
        new(LaunchAnimation.Pulse, "Pulsing logo"),
        new(LaunchAnimation.None, "None")
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
            ColorError = "Use #RGB or #RRGGBB, e.g. #7C6CFF.";
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
        if (files.OpenFile("Choose a loading screen image", filter) is not { } path) return;
        if (screen.SetImage(path) is { } error) dialogs.Show("Couldn't use that image", error, DialogKind.Warning);
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
