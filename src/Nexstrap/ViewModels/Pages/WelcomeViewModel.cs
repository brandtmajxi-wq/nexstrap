using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexstrap.Core;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Navigation;
using Nexstrap.Services.Roblox;

namespace Nexstrap.ViewModels.Pages;

/// <summary>A greeting, the PC's clock and a landscape. It reads nothing but the local time, so no location is involved.</summary>
public sealed partial class WelcomeViewModel : PageViewModel
{
    private static readonly Dictionary<string, ImageSource> Images = [];

    private readonly ILaunchCoordinator _launch;
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public WelcomeViewModel(ILaunchCoordinator launch, INavigationService navigation, ISettingsService settings)
    {
        _settings = settings;
        _launch = launch;
        _navigation = navigation;
        _clock.Tick += (_, _) => UpdateClock();
        settings.Saved += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(Greeting));
            OnPropertyChanged(nameof(Backdrop));
            foreach (var name in new[] { nameof(IsMoscow), nameof(IsBerlin), nameof(HasFlag), nameof(FlagTop), nameof(FlagMiddle), nameof(FlagBottom), nameof(Sky) })
                OnPropertyChanged(name);
            UpdateClock();
        });
        UpdateClock();
    }

    public override string Title => Loc.Get("Nav.Welcome");

    public override string Subtitle => Loc.Get("Page.Welcome");

    public override bool HasHeader => false;

    public override ImageSource Backdrop => ImageFor(City is { } city
        ? $"Resources/Welcome/{city}-{Part.ToString().ToLowerInvariant()}.jpg"
        : ImageFile(Part, Winter));

    /// <summary>Moscow's animated weather, unless it's switched off.</summary>
    public override SkyScene? Sky => MoscowSky(City == "moscow", _settings.Current.Welcome.MoscowWeather, Part);

    internal static SkyScene? MoscowSky(bool moscow, Weather setting, DayPart part) =>
        moscow && MoscowWeather.Resolve(setting, DateTime.Now) is var weather && weather != Weather.Off ? new SkyScene(weather, part) : null;

    internal static bool ShowsMoscow(Nexstrap.Models.AppSettings settings) => settings.SecretRoomUnlocked && settings.Welcome.MoscowScene;

    /// <summary>Berlin is dev only for now: public builds don't have its pictures.</summary>
    public static bool BerlinAvailable => AppInfo.IsDev;

    internal static bool ShowsBerlin(Nexstrap.Models.AppSettings settings, bool available) => available && settings.Welcome.BerlinScene;

    /// <summary>"berlin", "moscow" or null for the normal landscapes.</summary>
    private string? City => ShowsBerlin(_settings.Current, BerlinAvailable) ? "berlin" : IsMoscow ? "moscow" : null;

    public bool IsBerlin => City == "berlin";

    public bool HasFlag => City is not null;

    public Brush FlagTop => IsBerlin ? FlagBrush("#111111") : Brushes.White;

    public Brush FlagMiddle => FlagBrush(IsBerlin ? "#DD0000" : "#0039A6");

    public Brush FlagBottom => FlagBrush(IsBerlin ? "#FFCE00" : "#D52B1E");

    private static Brush FlagBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    internal static string GermanGreeting(DayPart part, string? name)
    {
        var hello = part switch
        {
            DayPart.Morning => "Guten Morgen",
            DayPart.Afternoon => "Guten Tag",
            DayPart.Evening => "Guten Abend",
            _ => "Gute Nacht"
        };
        return string.IsNullOrWhiteSpace(name) ? hello + "!" : $"{hello}, {name}!";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Greeting), nameof(Backdrop), nameof(Sky))]
    private DayPart _part;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Backdrop))]
    private bool _winter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Greeting))]
    private Holiday _holiday;

    [ObservableProperty]
    private string _time = "";

    [ObservableProperty]
    private string _date = "";

    public string Greeting => IsBerlin
        ? GermanGreeting(Part, _settings.Current.Welcome.Name)
        : IsMoscow
        ? SecretRoom.Greeting(Part, _settings.Current.Welcome.Name)
        : WelcomeName.Greet(GreetingFor(Part, Holiday), _settings.Current.Welcome.Name);

    /// <summary>The unlocked Moscow look: skyline, flag, a Russian greeting and Moscow's time.</summary>
    public bool IsMoscow => ShowsMoscow(_settings.Current);

    [ObservableProperty]
    private string _moscowLine = "";

    internal static string GreetingFor(DayPart part, Holiday holiday = Holiday.None) => (holiday, part) switch
    {
        (Holiday.Christmas, _) => Loc.Get("Welcome.Christmas"),
        (Holiday.NewYear, _) => Loc.Get("Welcome.NewYear"),
        (_, DayPart.Morning) => Loc.Get("Home.Morning"),
        (_, DayPart.Afternoon) => Loc.Get("Home.Afternoon"),
        (_, DayPart.Evening) => Loc.Get("Home.Evening"),
        _ => Loc.Get("Welcome.Night")
    };

    internal static string ImageFile(DayPart part, bool winter = false) =>
        $"Resources/Welcome/{(winter ? "winter-" : "")}{part.ToString().ToLowerInvariant()}.jpg";

    internal static ImageSource ImageFor(string file)
    {
        if (Images.TryGetValue(file, out var image)) return image;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri("pack://application:,,,/" + file);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return Images[file] = bitmap;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Part = TimeOfDay.Part(now.Hour);
        (Winter, Holiday) = WinterSeason.Resolve(_settings.Current.Winter, now, WinterSeason.Available);
        Time = now.ToString("T");
        Date = now.ToString("D");
        MoscowLine = !IsMoscow || IsBerlin ? "" : "Москва · " + SecretRoom.MoscowTime(DateTime.UtcNow).ToString("HH:mm", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
            + (Sky is { } sky ? " · " + MoscowWeather.RussianName(sky.Weather, sky.Part) : "");
    }

    public override void OnNavigatedTo()
    {
        UpdateClock();
        _clock.Start();
    }

    public override void OnNavigatedFrom() => _clock.Stop();

    [RelayCommand]
    private Task PlayAsync() => _launch.LaunchAppAsync();

    [RelayCommand]
    private void GoHome() => _navigation.NavigateTo<HomeViewModel>();
}
