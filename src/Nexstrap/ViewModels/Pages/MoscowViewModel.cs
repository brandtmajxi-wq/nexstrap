using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Navigation;

namespace Nexstrap.ViewModels.Pages;

/// <summary>A hidden page opened from About with a password: Moscow by its own time of day, with its clock and a red theme.</summary>
public sealed partial class MoscowViewModel : PageViewModel
{
    public const string ThemeAccent = "#D52B1E";

    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly ISettingsService _settings;
    private readonly IAppearanceService _appearance;
    private readonly INavigationService _navigation;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public MoscowViewModel(ISettingsService settings, IAppearanceService appearance, INavigationService navigation)
    {
        _settings = settings;
        _appearance = appearance;
        _navigation = navigation;
        _clock.Tick += (_, _) => UpdateClock();
        UpdateClock();
    }

    public override string Title => Loc.Get("Secret.Title");

    public override string Subtitle => Loc.Get("Secret.Subtitle");

    public bool IsUnlocked => _settings.Current.SecretRoomUnlocked;

    public override bool HasHeader => !IsUnlocked;

    public override ImageSource? Backdrop => IsUnlocked ? WelcomeViewModel.ImageFor($"Resources/Welcome/moscow-{Part.ToString().ToLowerInvariant()}.jpg") : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Backdrop), nameof(Greeting))]
    private DayPart _part;

    [ObservableProperty]
    private string _moscowTime = "";

    [ObservableProperty]
    private string _moscowDate = "";

    [ObservableProperty]
    private string _localTime = "";

    [ObservableProperty]
    private string _error = "";

    [ObservableProperty]
    private string _themeMessage = "";

    public string ThemeButton => "Красная тема";

    public string Greeting => SecretRoom.Greeting(Part, _settings.Current.Welcome.Name);

    private void UpdateClock()
    {
        var moscow = SecretRoom.MoscowTime(DateTime.UtcNow);
        Part = TimeOfDay.Part(moscow.Hour);
        MoscowTime = moscow.ToString("HH:mm:ss", Russian);
        MoscowDate = "Москва · " + moscow.ToString("dddd, d MMMM", Russian);
        LocalTime = "У тебя сейчас " + DateTime.Now.ToString("HH:mm", Russian);
    }

    /// <summary>Returns true if the password was right. The page stays open on this PC from then on.</summary>
    public bool TryUnlock(string password)
    {
        if (!SecretRoom.Check(password))
        {
            Error = Loc.Get("Secret.Wrong");
            return false;
        }

        Error = "";
        _settings.Current.SecretRoomUnlocked = true;
        _settings.Current.Welcome.MoscowScene = true;
        _settings.Save();
        foreach (var name in new[] { nameof(IsUnlocked), nameof(HasHeader), nameof(Backdrop) }) OnPropertyChanged(name);
        return true;
    }

    [RelayCommand]
    private void ApplyTheme()
    {
        _appearance.ApplyProfile(new ThemeProfile
        {
            Name = "Moscow",
            IsPreset = true,
            Appearance = new AppearanceSettings { Theme = AppTheme.Dark, AccentColor = ThemeAccent }
        });
        ThemeMessage = "Готово! Красная тема включена.";
    }

    [RelayCommand]
    private void Back() => _navigation.NavigateTo<AboutViewModel>();

    public override void OnNavigatedTo()
    {
        UpdateClock();
        _clock.Start();
    }

    public override void OnNavigatedFrom() => _clock.Stop();
}
