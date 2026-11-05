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
        UpdateClock();
    }

    public override string Title => Loc.Get("Nav.Welcome");

    public override string Subtitle => Loc.Get("Page.Welcome");

    public override bool HasHeader => false;

    public override ImageSource Backdrop => ImageFor(ImageFile(Part, Winter));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Greeting), nameof(Backdrop))]
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

    public string Greeting => GreetingFor(Part, Holiday);

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

    private static ImageSource ImageFor(string file)
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
