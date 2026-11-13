using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexstrap.Core;

namespace Nexstrap.Controls;

/// <summary>
/// Animated weather over the Moscow pictures: drifting clouds, a glowing sun with turning rays, birds,
/// twinkling stars, rain and downpours, thunderstorms, wind with blowing leaves, snow, blizzards and fog.
/// A gusting wind drives the clouds, the angle of the rain and the snow. Like the snow overlay it only
/// moves while the window is in front, follows Windows' "animation effects" switch and lets clicks through.
/// </summary>
public sealed class WeatherOverlay : FrameworkElement
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(SkyScene), typeof(WeatherOverlay),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((WeatherOverlay)d).Reset()));

    /// <summary>The weather and time of day to draw, or null for nothing.</summary>
    public SkyScene? Scene
    {
        get => (SkyScene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(
        nameof(Backdrop), typeof(ImageSource), typeof(WeatherOverlay), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The picture underneath, so the river part of it can be redrawn with moving ripples.</summary>
    public ImageSource? Backdrop
    {
        get => (ImageSource?)GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    // The backdrop pictures are 16:9 and drawn UniformToFill, so this layer maps its positions the same way.
    private const double PictureAspect = 16.0 / 9.0;
    private const double Horizon = 0.79;
    private const double CloudFloor = 0.5;
    private const int CloudWidth = 512, CloudHeight = 220;
    private const double CloudAspect = (double)CloudHeight / CloudWidth;
    private const double SplashLife = 0.35;
    /// <summary>Where the river starts in the pictures (WL in tools/make-moscow-images.ps1: 1152 of 1440).</summary>
    private const double WaterTop = 0.8;

    private struct Cloud { public double U, V, Speed, Size; public int Shape; }
    private struct RainDrop { public double X, Y, Speed, Length; public bool Near; }
    private struct Flake { public double X, Y, Size, Speed, Sway, Phase; }
    private struct Star { public double U, V, Size, Phase, Rate; }
    private struct Bird { public double U, V, Size, Phase; }
    private struct Band { public double U, V, Width, Height, Speed, Alpha; }
    private struct Streak { public double X, Y, Length, Speed, Phase, Alpha; }
    private struct Leaf { public double X, Y, Size, Angle, Spin, Speed, Fall, Phase; public int Colour; }
    private struct Splash { public double U, V, Age; }
    private struct Glint { public double Offset, V, Phase, Rate, Length; }

    /// <summary>The jet on a flyover: where it is and its size.</summary>
    private sealed class Jet
    {
        public double U, V, Size;
    }

    private readonly Random _random = new();
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };

    private Weather _weather = Weather.Off;
    private DayPart _part;
    private double _time, _gust;
    private bool _seeded;

    private BitmapSource[] _cloudShapes = [];
    private Cloud[] _clouds = [];
    private RainDrop[] _drops = [];
    private Flake[] _flakes = [];
    private Star[] _stars = [];
    private Band[] _fog = [];
    private Band[] _curtains = [];
    private Bird[] _birds = [];
    private Streak[] _streaks = [];
    private Leaf[] _leaves = [];
    private readonly List<Splash> _splashes = [];
    private Glint[] _glints = [];
    private Pen? _glintPen;

    // Jet flyovers (dev only for now).
    private bool _jetsOn;
    private readonly List<Jet> _jets = [];
    private double _jetDirection = 1, _jetSpeed, _jetSlope, _nextJets;
    private Brush? _jetBody, _jetWing;
    private double _splashRate, _splashBudget;
    private double _birdSpeed, _nextFlock;
    private double _shootingAge = 99, _nextShooting;
    private Point _shootingFrom, _shootingDir;
    private double _flashAge = 99, _nextFlash;
    private Point[][] _bolt = [];
    private double _sheetAge = 99, _nextSheet;
    private Point _sheetAt;

    private double _cloudOpacity;
    private Brush? _veil, _dim, _glow, _rays, _fogBrush, _curtainBrush, _flashBrush, _sheetBrush, _streakBrush;
    private Pen? _rainFar, _rainNear, _birdPen, _splashPen;
    private Brush[] _flakeShades = [];
    private Brush[] _leafBrushes = [];

    public WeatherOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
        _timer.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) => UpdateRunning();
        Loaded += (_, _) =>
        {
            LowEndMode.Changed += OnLowEndChanged;
            UpdateRunning();
        };
        Unloaded += (_, _) =>
        {
            LowEndMode.Changed -= OnLowEndChanged;
            _timer.Stop();
        };
    }

    private void OnLowEndChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateRunning);

    private bool Active => _weather is not (Weather.Off or Weather.Random);

    private void Reset()
    {
        _weather = Scene?.Weather ?? Weather.Off;
        _part = Scene?.Part ?? DayPart.Afternoon;
        _jetsOn = Scene?.Jets ?? false;
        _seeded = false;
        UpdateRunning();
    }

    private void UpdateRunning()
    {
        if (IsVisible && Active && LowEndMode.Animate) { _clock.Restart(); _timer.Start(); }
        else _timer.Stop();
        InvalidateVisual();
    }

    private void Advance()
    {
        var seconds = Math.Min(_clock.Elapsed.TotalSeconds, 0.1);
        _clock.Restart();
        if (!_seeded || Window.GetWindow(this) is not { IsActive: true, WindowState: not WindowState.Minimized }) return;
        Step(seconds);
        InvalidateVisual();
    }

    // ===== Layout: where the picture is inside this element =====

    private Rect Picture()
    {
        double w = ActualWidth, h = ActualHeight;
        var scale = Math.Max(w / PictureAspect, h);
        var pw = scale * PictureAspect;
        return new Rect((w - pw) / 2, (h - scale) / 2, pw, scale);
    }

    private static Point At(Rect picture, double u, double v) => new(picture.X + u * picture.Width, picture.Y + v * picture.Height);

    /// <summary>Where the sun or moon is drawn in each Moscow picture (tools/make-moscow-images.ps1), and its radius, as parts of the picture.</summary>
    internal static (double U, double V, double R) Sun(DayPart part) => part switch
    {
        DayPart.Night => (0.62, 0.16, 0.045),
        DayPart.Morning => (0.30, 0.50, 0.034),
        DayPart.Afternoon => (0.80, 0.12, 0.028),
        _ => (0.18, 0.58, 0.04)
    };

    // ===== Wind =====

    /// <summary>How windy each weather is on average: 0 is still air, 1 a thunderstorm.</summary>
    internal static double BaseWind(Weather weather) => weather switch
    {
        Weather.Clear => 0.2, Weather.PartlyCloudy => 0.3, Weather.Cloudy => 0.4, Weather.Rain => 0.5,
        Weather.HeavyRain => 0.8, Weather.Storm => 1.0, Weather.Wind => 1.4, Weather.Snow => 0.3,
        Weather.Blizzard => 1.6, _ => 0.1
    };

    internal static bool Gusty(Weather weather) => weather is Weather.HeavyRain or Weather.Storm or Weather.Wind or Weather.Blizzard;

    /// <summary>The wind at a moment: its average plus slow gusts from a few mixed waves, so it never quite repeats.</summary>
    internal static double Gust(Weather weather, double time)
    {
        var wave = 0.5 * Math.Sin(time * 0.37) + 0.3 * Math.Sin(time * 1.13 + 1) + 0.2 * Math.Sin(time * 2.9 + 2);
        return Math.Max(0.05, BaseWind(weather) * (1 + (Gusty(weather) ? 0.45 : 0.1) * wave));
    }

    /// <summary>How far rain leans: more wind, more slant.</summary>
    private double Slant => 0.06 + _gust * 0.3;

    private double CloudPace => 0.5 + _gust * 1.5;

    // ===== Setting up a scene =====

    private void Seed()
    {
        _seeded = true;
        _time = 0;
        if (!Active || ActualWidth <= 0 || ActualHeight <= 0) return;

        var w = _weather;
        _gust = Gust(w, 0);
        var overcast = w is Weather.Cloudy or Weather.Rain or Weather.HeavyRain or Weather.Storm or Weather.Snow or Weather.Blizzard or Weather.Fog;
        var hazy = w is Weather.Fog or Weather.Blizzard;
        var winter = DateTime.Now.Month is 12 or 1 or 2;

        // Cloud colours start from the picture's light and turn greyer and darker with the weather.
        var (lit, shade) = _part switch
        {
            DayPart.Morning => (Hex("#FFF1E6"), Hex("#C7B1BF")),
            DayPart.Afternoon => (Hex("#FFFFFF"), Hex("#C2CDDA")),
            DayPart.Evening => (Hex("#FFC6A6"), Hex("#6A4A6E")),
            _ => (Hex("#5A6280"), Hex("#22273A"))
        };
        var (grey, dark) = w switch
        {
            Weather.Cloudy => (0.45, 0.9),
            Weather.Fog => (0.6, 1.0),
            Weather.Snow => (0.5, 0.86),
            Weather.Blizzard => (0.6, 0.92),
            Weather.Rain => (0.62, 0.7),
            Weather.HeavyRain => (0.68, 0.58),
            Weather.Storm => (0.72, 0.48),
            Weather.Wind => (0.12, 0.97),
            _ => (0.0, 1.0)
        };
        var greyTone = _part == DayPart.Night ? Hex("#3A3F4E") : Hex("#A9AFB8");
        lit = Darken(Mix(lit, greyTone, grey), dark);
        shade = Darken(Mix(shade, greyTone, grey * 0.6), dark);
        _cloudShapes = Enumerable.Range(0, 5).Select(_ => MakeCloud(lit, shade)).ToArray();

        _cloudOpacity = w switch { Weather.Clear => 0.5, Weather.PartlyCloudy or Weather.Wind => 0.85, Weather.Fog => 0.5, _ => 0.95 };
        var count = w switch
        {
            Weather.Clear => 3, Weather.PartlyCloudy => 7, Weather.Wind => 11, Weather.Cloudy => 18, Weather.Rain => 20,
            Weather.HeavyRain => 22, Weather.Storm => 22, Weather.Snow => 16, Weather.Blizzard => 18, _ => 5
        };
        // Clouds stay in the sky: their bottom edge never reaches the Kremlin walls.
        _clouds = Enumerable.Range(0, count).Select(_ =>
        {
            var size = overcast ? 0.26 + _random.NextDouble() * 0.2 : 0.13 + _random.NextDouble() * 0.15;
            var tallest = size * PictureAspect * CloudAspect;
            var lowest = CloudFloor - tallest * 0.78;
            var highest = overcast ? -tallest * 0.55 : 0.0;
            return new Cloud
            {
                U = _random.NextDouble() * 1.3 - 0.3,
                V = highest + _random.NextDouble() * Math.Max(0.02, lowest - highest),
                Size = size,
                Speed = 0.004 + _random.NextDouble() * 0.008,
                Shape = _random.Next(5)
            };
        }).OrderBy(c => c.Size).ToArray();

        // A grey sky over the picture's own sky for cloudy weather. It thins out over the buildings so they stay readable.
        var veilAlpha = w switch
        {
            Weather.Cloudy => 0.62, Weather.Fog => 0.7, Weather.Snow => 0.7, Weather.Blizzard => 0.8,
            Weather.Rain => 0.8, Weather.HeavyRain => 0.86, Weather.Storm => 0.88, _ => 0.0
        };
        var veilColour = hazy
            ? _part == DayPart.Night ? Hex("#2E3442") : Hex("#C9CDD3")
            : _part switch
            {
                DayPart.Morning => Hex("#A29DA7"), DayPart.Afternoon => Hex("#949BA5"),
                DayPart.Evening => Hex("#4D4058"), _ => Hex("#0D111B")
            };
        if (w == Weather.Storm) veilColour = Darken(veilColour, 0.62);
        if (w == Weather.HeavyRain) veilColour = Darken(veilColour, 0.8);
        _veil = veilAlpha > 0 ? Freeze(new LinearGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(veilColour, veilAlpha), 0),
            new(WithAlpha(veilColour, veilAlpha * 0.95), 0.42),
            new(WithAlpha(veilColour, veilAlpha * (hazy ? 0.75 : 0.45)), Horizon - 0.05),
            new(WithAlpha(veilColour, veilAlpha * (hazy ? 0.6 : 0.25)), Horizon + 0.03),
            new(WithAlpha(veilColour, veilAlpha * (hazy ? 0.45 : 0.18)), 1)
        }, 90)) : null;
        var dimAlpha = w switch { Weather.Storm => 0.22, Weather.HeavyRain => 0.16, Weather.Rain => 0.1, _ => 0.0 };
        _dim = dimAlpha > 0 ? Freeze(new SolidColorBrush(WithAlpha(Colors.Black, dimAlpha))) : null;

        // Sun or moon glow and rays.
        var sunColour = _part switch
        {
            DayPart.Morning => Hex("#FFD9A0"), DayPart.Afternoon => Hex("#FFF6D6"),
            DayPart.Evening => Hex("#FFB070"), _ => Hex("#CFE0FF")
        };
        _glow = Freeze(new RadialGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(sunColour, _part == DayPart.Night ? 0.32 : 0.55), 0),
            new(WithAlpha(sunColour, _part == DayPart.Night ? 0.1 : 0.18), 0.35),
            new(WithAlpha(sunColour, 0), 1)
        }));
        _rays = Freeze(new RadialGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(sunColour, 0.3), 0),
            new(WithAlpha(sunColour, 0.1), 0.45),
            new(WithAlpha(sunColour, 0), 1)
        }));

        // Glitter on the river under the sun or moon, widening towards the bottom.
        _glints = w is Weather.Clear or Weather.PartlyCloudy or Weather.Wind
            ? Enumerable.Range(0, _part == DayPart.Night ? 50 : 70).Select(_ =>
              {
                  var spread = _random.NextDouble() * 2 - 1;
                  return new Glint
                  {
                      Offset = spread * Math.Abs(spread), V = WaterTop + 0.008 + _random.NextDouble() * (0.99 - WaterTop),
                      Phase = _random.NextDouble() * 6.28, Rate = 1.5 + _random.NextDouble() * 3, Length = 6 + _random.NextDouble() * 14
                  };
              }).ToArray()
            : [];
        _glintPen = Freeze(new Pen(new SolidColorBrush(Mix(sunColour, Colors.White, 0.5)), 1.6)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
        });

        // Night sky: stars that twinkle, kept away from the moon.
        var sun = Sun(_part);
        _stars = _part == DayPart.Night && w is Weather.Clear or Weather.PartlyCloudy or Weather.Wind
            ? Enumerable.Range(0, 140).Select(_ => new Star
              {
                  U = _random.NextDouble(), V = _random.NextDouble() * 0.6,
                  Size = 0.5 + _random.NextDouble() * 1.1, Phase = _random.NextDouble() * 6.28, Rate = 0.8 + _random.NextDouble() * 2.4
              }).Where(s => Math.Abs(s.U - sun.U) > 0.06 || Math.Abs(s.V - sun.V) > 0.1).ToArray()
            : [];
        _nextShooting = 3 + _random.NextDouble() * 6;
        _shootingAge = 99;

        // Birds cross on fair days.
        _birds = [];
        _nextFlock = 2 + _random.NextDouble() * 6;
        _birdPen = Freeze(new Pen(new SolidColorBrush(_part == DayPart.Evening ? Hex("#2A1A2C") : Hex("#2E3442")) { Opacity = 0.75 }, 1.7)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
        });

        // Rain, ripples on the river, and grey curtains of rain sweeping past in a downpour.
        var drops = w switch { Weather.Rain => 300, Weather.Storm => 560, Weather.HeavyRain => 720, _ => 0 };
        _drops = Enumerable.Range(0, drops).Select(_ => NewDrop(true)).ToArray();
        var rain = _part == DayPart.Night ? Hex("#8EA3C4") : Hex("#D2E2F2");
        var heavy = w is Weather.HeavyRain or Weather.Storm;
        _rainFar = Freeze(new Pen(new SolidColorBrush(WithAlpha(rain, heavy ? 0.26 : 0.22)), heavy ? 1.0 : 0.9));
        _rainNear = Freeze(new Pen(new SolidColorBrush(WithAlpha(rain, heavy ? 0.5 : 0.42)), heavy ? 1.5 : 1.3));
        _splashes.Clear();
        _splashRate = w switch { Weather.Rain => 25, Weather.Storm => 50, Weather.HeavyRain => 80, _ => 0 };
        _splashPen = Freeze(new Pen(new SolidColorBrush(WithAlpha(rain, 0.7)), 1));
        _curtains = heavy
            ? Enumerable.Range(0, w == Weather.HeavyRain ? 4 : 3).Select(_ => new Band
              {
                  U = _random.NextDouble() * 1.4 - 0.4, Width = 0.15 + _random.NextDouble() * 0.25,
                  Speed = 0.02 + _random.NextDouble() * 0.03, Alpha = 0.5 + _random.NextDouble() * 0.5
              }).ToArray()
            : [];
        var curtain = _part == DayPart.Night ? Hex("#3A4252") : Hex("#B4BCC6");
        _curtainBrush = Freeze(new LinearGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(curtain, 0), 0), new(WithAlpha(curtain, 0.22), 0.5), new(WithAlpha(curtain, 0), 1)
        }, 0));

        // Lightning: close strikes with a bolt, and distant flickers inside the clouds.
        _flashAge = 99;
        _nextFlash = 1.5 + _random.NextDouble() * 4;
        _sheetAge = 99;
        _nextSheet = 0.8 + _random.NextDouble() * 2;
        _flashBrush = Freeze(new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(255, 225, 232, 255), 0), new(Color.FromArgb(180, 225, 232, 255), 0.6),
            new(Color.FromArgb(90, 225, 232, 255), Horizon), new(Color.FromArgb(130, 225, 232, 255), 1)
        }, 90));
        _sheetBrush = Freeze(new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(150, 220, 228, 255), 0), new(Color.FromArgb(50, 200, 210, 255), 0.45), new(Color.FromArgb(0, 200, 210, 255), 1)
        }));

        // Snow, a blizzard, or snow dust blown about on a windy winter day.
        var flakes = w switch { Weather.Snow => 190, Weather.Blizzard => 420, Weather.Wind when winter => 110, _ => 0 };
        _flakes = Enumerable.Range(0, flakes).Select(_ =>
        {
            var depth = _random.NextDouble();
            return w switch
            {
                Weather.Blizzard => new Flake
                {
                    X = _random.NextDouble() * ActualWidth, Y = _random.NextDouble() * ActualHeight,
                    Size = 0.8 + depth * 1.8, Speed = 60 + depth * 140, Sway = 10 + _random.NextDouble() * 30, Phase = _random.NextDouble() * 6.28
                },
                Weather.Wind => new Flake
                {
                    X = _random.NextDouble() * ActualWidth, Y = _random.NextDouble() * ActualHeight,
                    Size = 0.6 + depth * 0.8, Speed = 8 + depth * 22, Sway = 20 + _random.NextDouble() * 30, Phase = _random.NextDouble() * 6.28
                },
                _ => new Flake
                {
                    X = _random.NextDouble() * ActualWidth, Y = _random.NextDouble() * ActualHeight,
                    Size = 0.9 + depth * 2.0, Speed = 26 + depth * 70, Sway = 8 + _random.NextDouble() * 20, Phase = _random.NextDouble() * 6.28
                }
            };
        }).ToArray();
        _flakeShades = [Freeze(new SolidColorBrush(WithAlpha(Colors.White, 0.5))), Freeze(new SolidColorBrush(WithAlpha(Colors.White, 0.75))), Freeze(new SolidColorBrush(WithAlpha(Colors.White, 0.95)))];

        // Fog banks rolling low over the river, or gusts of snow racing past in a blizzard.
        _fog = w switch
        {
            Weather.Fog => Enumerable.Range(0, 7).Select(i => new Band
            {
                U = _random.NextDouble() * 1.4 - 0.4, V = 0.5 + i * 0.065 + _random.NextDouble() * 0.04,
                Width = 0.8 + _random.NextDouble() * 0.7, Height = 0.12 + _random.NextDouble() * 0.1,
                Speed = 0.003 + _random.NextDouble() * 0.006, Alpha = 0.55 + _random.NextDouble() * 0.35
            }).ToArray(),
            Weather.Blizzard => Enumerable.Range(0, 6).Select(i => new Band
            {
                U = _random.NextDouble() * 1.4 - 0.4, V = 0.25 + i * 0.12 + _random.NextDouble() * 0.05,
                Width = 0.5 + _random.NextDouble() * 0.5, Height = 0.12 + _random.NextDouble() * 0.12,
                Speed = 0.03 + _random.NextDouble() * 0.03, Alpha = 0.35 + _random.NextDouble() * 0.3
            }).ToArray(),
            _ => []
        };
        var fogColour = _part == DayPart.Night ? Hex("#5A6378") : Hex("#E6E9EE");
        _fogBrush = Freeze(new RadialGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(fogColour, 0.7), 0), new(WithAlpha(fogColour, 0.4), 0.5), new(WithAlpha(fogColour, 0), 1)
        }));

        // Wind streaks: thin lines of moving air.
        var streaks = w switch { Weather.Wind => 26, Weather.Blizzard => 30, Weather.Storm => 10, Weather.HeavyRain => 6, _ => 0 };
        _streaks = Enumerable.Range(0, streaks).Select(_ => NewStreak(true)).ToArray();
        _streakBrush = Freeze(new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 255, 255, 255), 0), new(Color.FromArgb(255, 255, 255, 255), 0.5), new(Color.FromArgb(0, 255, 255, 255), 1)
        }, 0));

        // Leaves tumbling on the wind (not in winter, when there are none left).
        _leaves = w == Weather.Wind && !winter
            ? Enumerable.Range(0, 38).Select(_ => new Leaf
              {
                  X = _random.NextDouble() * ActualWidth, Y = _random.NextDouble() * ActualHeight * 0.95,
                  Size = 4.5 + _random.NextDouble() * 4.5, Angle = _random.NextDouble() * 360, Spin = (_random.NextDouble() - 0.5) * 600,
                  Speed = 260 + _random.NextDouble() * 280, Fall = 12 + _random.NextDouble() * 40, Phase = _random.NextDouble() * 6.28,
                  Colour = _random.Next(4)
              }).ToArray()
            : [];
        var shadeLeaves = _part switch { DayPart.Night => 0.45, DayPart.Evening => 0.75, _ => 1.0 };
        _leafBrushes = new[] { "#C8642A", "#E0A030", "#8A3A1E", "#7C8F2E" }
            .Select(c => (Brush)Freeze(new SolidColorBrush(Darken(Hex(c), shadeLeaves)))).ToArray();

        // Jets: a single fighter every 30 seconds on fair days, the first one soon after the page opens.
        _jets.Clear();
        _nextJets = 5;
        var (body, wing) = _part switch
        {
            DayPart.Night => (Hex("#1A1D26"), Hex("#11131A")),
            DayPart.Evening => (Hex("#2B2233"), Hex("#1E1826")),
            DayPart.Morning => (Hex("#4A4C5A"), Hex("#363845")),
            _ => (Hex("#4B525E"), Hex("#363B45"))
        };
        _jetBody = Freeze(new SolidColorBrush(body));
        _jetWing = Freeze(new SolidColorBrush(wing));
    }

    /// <summary>Jets fly in fair weather.</summary>
    private bool JetsFly => _jetsOn && _weather is Weather.Clear or Weather.PartlyCloudy or Weather.Wind or Weather.Cloudy;

    private void LaunchJets()
    {
        _jets.Clear();
        _jetDirection = _random.NextDouble() < 0.5 ? 1 : -1;
        _jetSlope = (_random.NextDouble() - 0.5) * 0.06;
        _jetSpeed = 0.15 + _random.NextDouble() * 0.04;
        _jets.Add(new Jet { U = _jetDirection > 0 ? -0.08 : 1.08, V = 0.08 + _random.NextDouble() * 0.24, Size = 0.075 });
    }

    private void StepJets(double seconds)
    {
        if (!JetsFly) { _jets.Clear(); return; }

        // A new pass every 30 seconds, counted from the start of the last one.
        _nextJets -= seconds;
        if (_jets.Count > 0)
        {
            foreach (var jet in _jets)
            {
                jet.U += _jetDirection * _jetSpeed * seconds;
                jet.V += _jetSlope * _jetSpeed * seconds;
            }
            if (_jets.All(j => _jetDirection > 0 ? j.U > 1.12 : j.U < -0.12)) _jets.Clear();
        }
        else if (_nextJets <= 0)
        {
            LaunchJets();
            _nextJets = JetInterval;
        }
    }

    /// <summary>Seconds from one pass to the next. A pass takes under 10 seconds.</summary>
    internal const double JetInterval = 30;

    private RainDrop NewDrop(bool anywhere)
    {
        var near = _random.NextDouble() < 0.35;
        var lean = ActualHeight * (0.06 + BaseWind(_weather) * 0.45);
        var extra = _weather switch { Weather.Storm => 250, Weather.HeavyRain => 200, _ => 0 };
        var longer = _weather == Weather.HeavyRain ? 1.4 : 1.0;
        return new RainDrop
        {
            X = _random.NextDouble() * (ActualWidth + lean) - lean,
            Y = anywhere ? _random.NextDouble() * ActualHeight : -40 - _random.NextDouble() * 60,
            Speed = (near ? 1150 : 820) + _random.NextDouble() * 300 + extra,
            Length = ((near ? 22 : 13) + _random.NextDouble() * 10) * longer,
            Near = near
        };
    }

    private Streak NewStreak(bool anywhere) => new()
    {
        X = anywhere ? _random.NextDouble() * ActualWidth : -300 - _random.NextDouble() * 400,
        Y = _random.NextDouble() * ActualHeight * 0.9,
        Length = 120 + _random.NextDouble() * 260,
        Speed = 500 + _random.NextDouble() * 500,
        Phase = _random.NextDouble() * 6.28,
        Alpha = 0.2 + _random.NextDouble() * 0.3
    };

    // ===== Moving things along =====

    private void Step(double seconds)
    {
        _time += seconds;
        _gust = Gust(_weather, _time);
        double w = ActualWidth, h = ActualHeight;

        for (var i = 0; i < _clouds.Length; i++)
        {
            ref var c = ref _clouds[i];
            c.U += c.Speed * CloudPace * seconds;
            if (c.U > 1.05) { c.U = -c.Size - _random.NextDouble() * 0.2; c.Shape = _random.Next(_cloudShapes.Length); }
        }

        for (var i = 0; i < _drops.Length; i++)
        {
            ref var d = ref _drops[i];
            d.Y += d.Speed * seconds;
            d.X += d.Speed * Slant * seconds;
            if (d.Y > h + d.Length || d.X > w + 40) d = NewDrop(false);
        }

        // Ripples where rain hits the river.
        for (var i = _splashes.Count - 1; i >= 0; i--)
        {
            var s = _splashes[i];
            s.Age += seconds;
            if (s.Age > SplashLife) _splashes.RemoveAt(i);
            else _splashes[i] = s;
        }
        _splashBudget += _splashRate * seconds;
        while (_splashBudget >= 1)
        {
            _splashBudget--;
            _splashes.Add(new Splash { U = _random.NextDouble(), V = Horizon + 0.015 + _random.NextDouble() * (0.99 - Horizon - 0.015) });
        }

        for (var i = 0; i < _curtains.Length; i++)
        {
            ref var c = ref _curtains[i];
            c.U += c.Speed * CloudPace * seconds;
            if (c.U > 1.2) c.U = -c.Width - 0.2 - _random.NextDouble() * 0.3;
        }

        var drift = _weather == Weather.Blizzard ? 120 + _gust * 260 : 10 + _gust * 140;
        for (var i = 0; i < _flakes.Length; i++)
        {
            ref var f = ref _flakes[i];
            f.Y += f.Speed * seconds;
            f.Phase += seconds;
            f.X += (Math.Sin(f.Phase * 0.9) * f.Sway + drift) * seconds;
            if (f.Y > h + f.Size) { f.Y = -f.Size; f.X = _random.NextDouble() * w; }
            if (f.X > w + f.Size) { f.X = -f.Size; f.Y = _random.NextDouble() * h; }
        }

        for (var i = 0; i < _fog.Length; i++)
        {
            ref var b = ref _fog[i];
            b.U += b.Speed * (_weather == Weather.Blizzard ? CloudPace : 1) * seconds;
            if (b.U > 1.1) b.U = -b.Width - 0.1;
        }

        for (var i = 0; i < _streaks.Length; i++)
        {
            ref var s = ref _streaks[i];
            s.X += s.Speed * (0.4 + _gust * 0.6) * seconds;
            s.Phase += seconds * 2.2;
            if (s.X > w + 20) s = NewStreak(false);
        }

        for (var i = 0; i < _leaves.Length; i++)
        {
            ref var l = ref _leaves[i];
            l.Phase += seconds * 3;
            l.X += l.Speed * (0.3 + _gust * 0.6) * seconds;
            l.Y += (l.Fall + Math.Sin(l.Phase) * 60) * seconds;
            l.Angle += l.Spin * (0.5 + _gust) * seconds;
            if (l.X > w + 20) { l.X = -20 - _random.NextDouble() * 200; l.Y = _random.NextDouble() * h * 0.9; }
            if (l.Y > h + 20) { l.Y = -20; l.X = _random.NextDouble() * w; }
        }

        // Birds: a small flock now and then on fair days.
        if (_part != DayPart.Night && _weather is Weather.Clear or Weather.PartlyCloudy)
        {
            for (var i = 0; i < _birds.Length; i++)
            {
                _birds[i].U += _birdSpeed * seconds;
                _birds[i].Phase += seconds * 9;
            }
            if (_birds.Length > 0 && _birds.All(b => b.U > 1.1)) _birds = [];
            if (_birds.Length == 0 && (_nextFlock -= seconds) <= 0)
            {
                var v = 0.12 + _random.NextDouble() * 0.3;
                _birdSpeed = 0.035 + _random.NextDouble() * 0.025;
                _birds = Enumerable.Range(0, 3 + _random.Next(4)).Select(k => new Bird
                {
                    U = -0.05 - k * 0.025 - _random.NextDouble() * 0.02,
                    V = v + (k % 2 == 0 ? 1 : -1) * k * 0.012 + _random.NextDouble() * 0.01,
                    Size = 5 + _random.NextDouble() * 4, Phase = _random.NextDouble() * 6.28
                }).ToArray();
                _nextFlock = 14 + _random.NextDouble() * 22;
            }
        }

        // Shooting stars on clear nights.
        _shootingAge += seconds;
        if (_stars.Length > 0 && (_nextShooting -= seconds) <= 0)
        {
            _shootingAge = 0;
            _shootingFrom = new Point(0.15 + _random.NextDouble() * 0.7, 0.04 + _random.NextDouble() * 0.22);
            _shootingDir = new Point((_random.NextDouble() < 0.5 ? -1 : 1) * 0.32, 0.16);
            _nextShooting = 6 + _random.NextDouble() * 12;
        }

        StepJets(seconds);

        // Lightning in storms: a strike every few seconds, and flickers deep in the clouds in between.
        _flashAge += seconds;
        _sheetAge += seconds;
        if (_weather == Weather.Storm)
        {
            if ((_nextFlash -= seconds) <= 0)
            {
                _flashAge = 0;
                _bolt = MakeBolt();
                _nextFlash = 3.5 + _random.NextDouble() * 7;
            }
            if ((_nextSheet -= seconds) <= 0)
            {
                _sheetAge = 0;
                _sheetAt = new Point(0.08 + _random.NextDouble() * 0.84, 0.04 + _random.NextDouble() * 0.3);
                _nextSheet = 1.2 + _random.NextDouble() * 3.5;
            }
        }
    }

    /// <summary>A jagged bolt from the clouds down to the skyline, with a few forks off it.</summary>
    private Point[][] MakeBolt()
    {
        var main = new List<Point>();
        var u = 0.2 + _random.NextDouble() * 0.75;
        var v = -0.02;
        main.Add(new Point(u, v));
        while (v < 0.62)
        {
            v += 0.03 + _random.NextDouble() * 0.045;
            u += (_random.NextDouble() - 0.5) * 0.05;
            main.Add(new Point(u, v));
        }

        var bolts = new List<Point[]> { main.ToArray() };
        var forks = 2 + _random.Next(3);
        for (var f = 0; f < forks; f++)
        {
            var from = main[1 + _random.Next(Math.Max(1, main.Count - 4))];
            var side = _random.NextDouble() < 0.5 ? -1 : 1;
            var fork = new List<Point> { from };
            double fu = from.X, fv = from.Y;
            for (var k = 0; k < 3 + _random.Next(3); k++)
            {
                fv += 0.025 + _random.NextDouble() * 0.035;
                fu += side * (0.012 + _random.NextDouble() * 0.025);
                fork.Add(new Point(fu, fv));
            }
            bolts.Add(fork.ToArray());
        }
        return bolts.ToArray();
    }

    /// <summary>How bright a lightning flash is, a moment after it struck: a strike, a flicker and a fade.</summary>
    internal static double Flash(double age) => age switch
    {
        < 0 => 0,
        < 0.07 => 1,
        < 0.15 => 0.15,
        < 0.24 => 0.75,
        _ => Math.Max(0, 0.75 * Math.Exp(-(age - 0.24) * 6))
    };

    // ===== Drawing =====

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        if (!_seeded) Seed();
        if (!Active) return;

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.PushClip(new RectangleGeometry(bounds));
        var pic = Picture();

        DrawWater(dc, pic, bounds);
        if (_dim is not null) dc.DrawRectangle(_dim, null, bounds);
        if (_veil is not null) dc.DrawRectangle(_veil, null, pic);

        DrawStars(dc, pic);
        DrawSun(dc, pic);
        DrawBirds(dc, pic);
        DrawClouds(dc, pic);
        DrawJets(dc, pic);
        DrawSheetLightning(dc, pic);
        DrawCurtains(dc, pic);
        DrawFog(dc, pic);
        DrawLightning(dc, pic, bounds);
        DrawRain(dc);
        DrawSplashes(dc, pic);
        DrawStreaks(dc);
        DrawLeaves(dc);
        DrawSnow(dc);

        dc.Pop();
    }

    /// <summary>
    /// The river: the picture's own water is redrawn in thin strips, each nudged sideways by a travelling wave,
    /// so the reflections wobble. Waves are bigger near the bottom (closer to you) and in strong wind.
    /// </summary>
    private void DrawWater(DrawingContext dc, Rect pic, Rect bounds)
    {
        var image = Backdrop;
        if (image is null) return;

        var top = Math.Max(bounds.Top, pic.Y + WaterTop * pic.Height);
        var bottom = Math.Min(bounds.Bottom, pic.Bottom);
        if (bottom <= top) return;

        var strip = Math.Max(1.5, pic.Height * 0.003);
        var scale = pic.Height / 1080 * 1.6;
        var wind = 0.6 + _gust * 0.7;
        for (var y = top; y < bottom; y += strip)
        {
            var v = (y - pic.Y) / pic.Height;
            var (dx, dy) = Ripple(v, _time, wind, scale);
            dc.PushClip(new RectangleGeometry(new Rect(bounds.X, y, bounds.Width, strip + 0.6)));
            dc.DrawImage(image, new Rect(pic.X + dx, pic.Y + dy, pic.Width, pic.Height));
            dc.Pop();
        }

        DrawGlints(dc, pic, scale);
    }

    /// <summary>How far a strip of river at height v (0–1 in the picture) is moved, at a moment. Calm at the bank, livelier further down.</summary>
    internal static (double Dx, double Dy) Ripple(double v, double time, double wind, double scale)
    {
        var depth = Math.Clamp((v - WaterTop) / (1 - WaterTop), 0, 1);
        var amplitude = (0.5 + depth * 4) * scale * Math.Min(wind, 1.5);
        var wave = 0.6 * Math.Sin(v * 260 - time * 1.7 * wind) + 0.4 * Math.Sin(v * 113 + time * 1.15 + depth * 3);
        var bob = Math.Sin(v * 190 - time * 2.1);
        return (amplitude * wave, amplitude * 0.25 * bob);
    }

    private void DrawGlints(DrawingContext dc, Rect pic, double scale)
    {
        if (_glints.Length == 0 || _glintPen is null) return;
        var sunU = Sun(_part).U;
        var strength = (_part == DayPart.Night ? 0.55 : 0.9) * (_weather == Weather.PartlyCloudy ? 0.7 : 1);
        foreach (var g in _glints)
        {
            var depth = (g.V - WaterTop) / (1 - WaterTop);
            var u = sunU + g.Offset * (0.03 + depth * 0.13) + Math.Sin(_time * 0.6 + g.Phase) * 0.004;
            var twinkle = Math.Max(0, Math.Sin(_time * g.Rate + g.Phase));
            if (twinkle < 0.05) continue;
            var at = At(pic, u, g.V);
            var half = g.Length * (0.5 + depth) * scale * 0.5 * (0.6 + 0.4 * twinkle);
            dc.PushOpacity(strength * twinkle * twinkle * twinkle);
            dc.DrawLine(_glintPen, new Point(at.X - half, at.Y), new Point(at.X + half, at.Y));
            dc.Pop();
        }
    }

    private void DrawSun(DrawingContext dc, Rect pic)
    {
        if (_weather is not (Weather.Clear or Weather.PartlyCloudy or Weather.Wind)) return;
        var (u, v, r) = Sun(_part);
        var centre = At(pic, u, v);
        var radius = r * pic.Height;

        // A soft glow that breathes.
        var glow = radius * (_part == DayPart.Night ? 4.5 : 7) * (1 + 0.05 * Math.Sin(_time * 0.9));
        dc.PushOpacity(0.75 + 0.2 * Math.Sin(_time * 0.7));
        dc.DrawEllipse(_glow, null, centre, glow, glow);
        dc.Pop();
        if (_part == DayPart.Night) return;

        // Two sets of rays turning slowly in opposite directions.
        var length = radius * (_part == DayPart.Evening ? 7 : 10);
        dc.PushOpacity(_weather == Weather.Clear ? 0.9 : 0.6);
        DrawRays(dc, centre, radius * 1.4, length, 12, _time * 0.05, 0.05);
        DrawRays(dc, centre, radius * 1.4, length * 0.75, 9, -_time * 0.035 + 0.3, 0.035);
        dc.Pop();
    }

    private void DrawRays(DrawingContext dc, Point centre, double inner, double length, int count, double turn, double width)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < count; i++)
            {
                var angle = turn + i * Math.Tau / count;
                var reach = length * (0.75 + 0.25 * Math.Sin(_time * 0.6 + i * 1.7));
                g.BeginFigure(Polar(centre, inner, angle - width * 2), true, true);
                g.LineTo(Polar(centre, reach, angle - width), false, false);
                g.LineTo(Polar(centre, reach, angle + width), false, false);
                g.LineTo(Polar(centre, inner, angle + width * 2), false, false);
            }
        }
        geometry.Freeze();
        // The brush fades with distance from the sun, so the rays' bounds must be centred on it.
        dc.PushClip(new EllipseGeometry(centre, length, length));
        dc.DrawGeometry(RaysAround(centre, length), null, geometry);
        dc.Pop();
    }

    private Brush RaysAround(Point centre, double length)
    {
        var brush = new RadialGradientBrush(((RadialGradientBrush)_rays!).GradientStops)
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = centre, GradientOrigin = centre, RadiusX = length, RadiusY = length
        };
        brush.Freeze();
        return brush;
    }

    private static Point Polar(Point centre, double distance, double angle) =>
        new(centre.X + Math.Cos(angle) * distance, centre.Y + Math.Sin(angle) * distance);

    private void DrawStars(DrawingContext dc, Rect pic)
    {
        foreach (var star in _stars)
        {
            var twinkle = 0.5 + 0.5 * Math.Sin(_time * star.Rate + star.Phase);
            dc.PushOpacity(0.15 + 0.85 * twinkle * twinkle);
            dc.DrawEllipse(Brushes.White, null, At(pic, star.U, star.V), star.Size, star.Size);
            dc.Pop();
        }

        if (_shootingAge < 0.9)
        {
            var t = _shootingAge / 0.9;
            var head = At(pic, _shootingFrom.X + _shootingDir.X * t, _shootingFrom.Y + _shootingDir.Y * t);
            var tail = At(pic, _shootingFrom.X + _shootingDir.X * Math.Max(0, t - 0.35), _shootingFrom.Y + _shootingDir.Y * Math.Max(0, t - 0.35));
            var brush = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(230, 255, 255, 255), tail, head) { MappingMode = BrushMappingMode.Absolute };
            dc.PushOpacity(Math.Sin(t * Math.PI));
            dc.DrawLine(new Pen(brush, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, tail, head);
            dc.Pop();
        }
    }

    private void DrawBirds(DrawingContext dc, Rect pic)
    {
        if (_birds.Length == 0 || _birdPen is null) return;
        var scale = pic.Height / 1080 * 1.3;
        foreach (var bird in _birds)
        {
            var p = At(pic, bird.U, bird.V);
            var s = bird.Size * scale;
            var flap = Math.Sin(bird.Phase) * s * 0.55;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(p.X - s, p.Y - flap), false, false);
                g.QuadraticBezierTo(new Point(p.X - s * 0.4, p.Y - s * 0.45 - flap * 0.3), p, true, true);
                g.QuadraticBezierTo(new Point(p.X + s * 0.4, p.Y - s * 0.45 - flap * 0.3), new Point(p.X + s, p.Y - flap), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, _birdPen, geometry);
        }
    }

    private void DrawClouds(DrawingContext dc, Rect pic)
    {
        if (_clouds.Length == 0) return;
        dc.PushOpacity(_cloudOpacity);
        foreach (var cloud in _clouds)
        {
            var shape = _cloudShapes[cloud.Shape % _cloudShapes.Length];
            var width = cloud.Size * pic.Width;
            var height = width * shape.PixelHeight / shape.PixelWidth;
            var at = At(pic, cloud.U, cloud.V);
            dc.DrawImage(shape, new Rect(at.X, at.Y, width, height));
        }
        dc.Pop();
    }

    // ===== Jets =====

    /// <summary>
    /// A side view of a modern twin-tail fighter (in the spirit of the Su-57), nose towards +x, 100 units long.
    /// Body, the far wing and tailplane are separate so they can be shaded a little differently.
    /// </summary>
    private static readonly Point[] JetBody =
    [
        new(100, 0), new(90, -2.2), new(80, -3.6), new(74, -6.8), new(68, -7.6), new(61, -5.6), new(44, -4.6),
        new(30, -4.8), new(19, -17), new(13, -17.5), new(10, -5), new(4, -4.4), new(0, -2.6), new(0, 2.2),
        new(7, 3.4), new(30, 4.4), new(58, 4.6), new(70, 3.4), new(86, 2.2)
    ];

    private static readonly Point[] JetWing = [new(64, 1), new(36, 2.4), new(22, 8.6), new(31, 8.6), new(60, 3)];

    private static readonly Point[] JetTail = [new(16, 1.2), new(3, 6), new(9, 6.4), new(22, 2.4)];

    private static readonly Point[] Su57Canopy = [new(74, -6.4), new(68, -7.2), new(63, -5.4), new(72, -4.6)];

    private void DrawJets(DrawingContext dc, Rect pic)
    {
        if (_jets.Count == 0 || _jetBody is null) return;

        foreach (var jet in _jets)
        {
            var tail = At(pic, jet.U - _jetDirection * jet.Size * 0.5 / PictureAspect, jet.V + 0.004);
            if (_part is DayPart.Morning or DayPart.Afternoon)
            {
                // A short, thin condensation trail behind a single jet.
                var back = At(pic, jet.U - _jetDirection * 0.22, jet.V - _jetSlope * 0.22);
                var brush = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(120, 255, 255, 255), back, tail)
                    { MappingMode = BrushMappingMode.Absolute };
                dc.DrawLine(new Pen(brush, 1.6 * pic.Height / 1080 * 1.4), back, tail);
            }
        }

        foreach (var jet in _jets)
        {
            var centre = At(pic, jet.U, jet.V);
            var length = jet.Size * pic.Height;
            var scale = length / 100;
            var angle = Math.Atan(_jetSlope) * 180 / Math.PI;
            dc.PushTransform(new TranslateTransform(centre.X, centre.Y));
            dc.PushTransform(new RotateTransform(_jetDirection > 0 ? angle : -angle));
            dc.PushTransform(new ScaleTransform(_jetDirection * scale, scale));
            dc.PushTransform(new TranslateTransform(-50, 0));
            dc.DrawGeometry(_jetWing, null, Polygon(JetTail));
            dc.DrawGeometry(_jetBody, null, Polygon(JetBody));
            dc.DrawGeometry(_jetWing, null, Polygon(JetWing));
            // Cockpit glint and the engine glow.
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(_part == DayPart.Night ? (byte)60 : (byte)110, 170, 200, 230)), null,
                Polygon(Su57Canopy));
            if (_part is DayPart.Evening or DayPart.Night)
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, 255, 150, 70)), null, new Point(-0.5, -0.2), 2.4, 1.8);
            if (_part == DayPart.Night)
            {
                // Navigation lights: red, green and a white strobe that blinks once a second.
                dc.DrawEllipse(Brushes.Red, null, new Point(26, 8.2), 1.4, 1.4);
                dc.DrawEllipse(Brushes.LimeGreen, null, new Point(30, 7.8), 1.2, 1.2);
                if (_time % 1.0 < 0.12) dc.DrawEllipse(Brushes.White, null, new Point(15, -17.6), 2.2, 2.2);
            }
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }
    }

    private static StreamGeometry Polygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], true, true);
            for (var i = 1; i < points.Count; i++) g.LineTo(points[i], false, true);
        }
        geometry.Freeze();
        return geometry;
    }

    private void DrawSheetLightning(DrawingContext dc, Rect pic)
    {
        var flash = Flash(_sheetAge);
        if (flash <= 0.01) return;
        var at = At(pic, _sheetAt.X, _sheetAt.Y);
        var r = pic.Width * 0.22;
        dc.PushOpacity(flash * 0.7);
        dc.DrawEllipse(_sheetBrush, null, at, r, r * 0.6);
        dc.Pop();
    }

    private void DrawCurtains(DrawingContext dc, Rect pic)
    {
        if (_curtains.Length == 0) return;
        var lean = Math.Atan(Slant) * 180 / Math.PI;
        foreach (var c in _curtains)
        {
            var left = At(pic, c.U, 0);
            var rect = new Rect(left.X, pic.Y, c.Width * pic.Width, pic.Height);
            dc.PushTransform(new SkewTransform(lean, 0, rect.X, rect.Y));
            dc.PushOpacity(c.Alpha * (0.8 + 0.2 * Math.Sin(_time * 0.8 + c.Width * 10)));
            dc.DrawRectangle(_curtainBrush, null, rect);
            dc.Pop();
            dc.Pop();
        }
    }

    private void DrawFog(DrawingContext dc, Rect pic)
    {
        foreach (var band in _fog)
        {
            var at = At(pic, band.U, band.V);
            dc.PushOpacity(band.Alpha * (0.85 + 0.15 * Math.Sin(_time * 0.3 + band.V * 20)));
            dc.DrawRectangle(_fogBrush, null, new Rect(at.X, at.Y - band.Height * pic.Height / 2, band.Width * pic.Width, band.Height * pic.Height));
            dc.Pop();
        }
    }

    private void DrawLightning(DrawingContext dc, Rect pic, Rect bounds)
    {
        var flash = Flash(_flashAge);
        if (flash <= 0.01) return;

        // The sky lights up most, the river catches some of it.
        dc.PushOpacity(flash * 0.32);
        dc.DrawRectangle(_flashBrush, null, bounds);
        dc.Pop();
        if (_flashAge > 0.35 || _bolt.Length == 0) return;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            foreach (var line in _bolt)
            {
                g.BeginFigure(At(pic, line[0].X, line[0].Y), false, false);
                foreach (var point in line.Skip(1)) g.LineTo(At(pic, point.X, point.Y), true, true);
            }
        }
        geometry.Freeze();
        dc.PushOpacity(Math.Min(1, flash * 1.3));
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(60, 170, 190, 255)), 14) { LineJoin = PenLineJoin.Round }, geometry);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(110, 200, 215, 255)), 6) { LineJoin = PenLineJoin.Round }, geometry);
        dc.DrawGeometry(null, new Pen(Brushes.White, 2.2) { LineJoin = PenLineJoin.Round }, geometry);
        dc.Pop();
    }

    private void DrawRain(DrawingContext dc)
    {
        if (_drops.Length == 0) return;
        foreach (var near in new[] { false, true })
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                foreach (var d in _drops)
                {
                    if (d.Near != near) continue;
                    g.BeginFigure(new Point(d.X, d.Y), false, false);
                    g.LineTo(new Point(d.X + d.Length * Slant, d.Y + d.Length), true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, near ? _rainNear : _rainFar, geometry);
        }
    }

    private void DrawSplashes(DrawingContext dc, Rect pic)
    {
        if (_splashes.Count == 0 || _splashPen is null) return;
        var scale = pic.Height / 1080;
        foreach (var s in _splashes)
        {
            var t = s.Age / SplashLife;
            var depth = (s.V - Horizon) / (1 - Horizon); // ripples nearer the bottom look bigger
            var r = (2 + t * 14) * (0.6 + depth) * scale * 1.4;
            dc.PushOpacity((1 - t) * 0.55);
            dc.DrawEllipse(null, _splashPen, At(pic, s.U, s.V), r, r * 0.28);
            dc.Pop();
        }
    }

    private void DrawStreaks(DrawingContext dc)
    {
        foreach (var s in _streaks)
        {
            var bend = Math.Sin(s.Phase) * 10;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(s.X, s.Y), false, false);
                g.BezierTo(new Point(s.X + s.Length * 0.33, s.Y - bend), new Point(s.X + s.Length * 0.66, s.Y + bend),
                    new Point(s.X + s.Length, s.Y + Math.Sin(s.Phase + 1) * 6), true, true);
            }
            geometry.Freeze();
            dc.PushOpacity(s.Alpha * (0.6 + _gust * 0.4));
            dc.DrawGeometry(null, new Pen(_streakBrush, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
            dc.Pop();
        }
    }

    private void DrawLeaves(DrawingContext dc)
    {
        foreach (var l in _leaves)
        {
            // Squashing the width as it turns makes it look like it's tumbling.
            var flip = 0.25 + 0.75 * Math.Abs(Math.Cos(l.Phase * 1.3));
            dc.PushTransform(new RotateTransform(l.Angle, l.X, l.Y));
            dc.DrawEllipse(_leafBrushes[l.Colour], null, new Point(l.X, l.Y), l.Size * 1.5 * flip, l.Size * 0.7);
            dc.Pop();
        }
    }

    private void DrawSnow(DrawingContext dc)
    {
        // In a blizzard the flakes smear sideways with the wind.
        var stretch = _weather == Weather.Blizzard ? 1 + _gust * 1.4 : 1;
        foreach (var f in _flakes)
            dc.DrawEllipse(_flakeShades[f.Size < 1.5 ? 0 : f.Size < 2.3 ? 1 : 2], null, new Point(f.X, f.Y), f.Size * stretch, f.Size);
    }

    // ===== Cloud pictures =====

    /// <summary>Paints one soft cloud from overlapping round puffs: a shaded underside, then the lit top.</summary>
    private BitmapSource MakeCloud(Color lit, Color shade)
    {
        // Many smaller puffs, flatter at the bottom, then a blur so the edges look like vapour rather than cotton.
        var puffs = Enumerable.Range(0, 12 + _random.Next(6)).Select(_ =>
        {
            var x = 80 + _random.NextDouble() * 352;
            var middle = 1 - Math.Abs(x - 256) / 256;
            var r = 20 + middle * 42 + _random.NextDouble() * 18;
            return (X: x, Y: 160 - r * 0.6 - _random.NextDouble() * 22 * middle, R: r);
        }).ToList();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var under = Puff(shade, 0.7);
            var top = Puff(lit, 0.85);
            foreach (var p in puffs) dc.DrawEllipse(under, null, new Point(p.X, p.Y + p.R * 0.25), p.R * 1.1, p.R * 0.8);
            foreach (var p in puffs) dc.DrawEllipse(top, null, new Point(p.X, p.Y - p.R * 0.1), p.R * 0.92, p.R * 0.78);
        }
        visual.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 9, RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality };
        var holder = new ContainerVisual();
        holder.Children.Add(visual);
        var bitmap = new RenderTargetBitmap(CloudWidth, CloudHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(holder);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush Puff(Color colour, double alpha) => Freeze(new RadialGradientBrush(new GradientStopCollection
    {
        new(WithAlpha(colour, alpha), 0),
        new(WithAlpha(colour, alpha * 0.8), 0.55),
        new(WithAlpha(colour, 0), 1)
    }) { GradientOrigin = new Point(0.45, 0.38) });

    // ===== Colour helpers =====

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double k) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));

    private static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
}
