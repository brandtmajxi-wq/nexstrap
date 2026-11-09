using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexstrap.Core;

namespace Nexstrap.Controls;

/// <summary>
/// Animated weather over the Moscow pictures: drifting clouds, a glowing sun with turning rays,
/// birds, twinkling stars, rain, thunderstorms, snow and fog. Like the snow overlay it only moves
/// while the window is in front, follows Windows' "animation effects" switch and lets clicks through.
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

    // The backdrop pictures are 16:9 and drawn UniformToFill, so this layer maps its positions the same way.
    private const double PictureAspect = 16.0 / 9.0;
    private const double Horizon = 0.79;
    private const double CloudFloor = 0.5;
    private const int CloudWidth = 512, CloudHeight = 220;
    private const double CloudAspect = (double)CloudHeight / CloudWidth;

    private struct Cloud { public double U, V, Speed, Size; public int Shape; }
    private struct RainDrop { public double X, Y, Speed, Length; public bool Near; }
    private struct Flake { public double X, Y, Size, Speed, Sway, Phase; }
    private struct Star { public double U, V, Size, Phase, Rate; }
    private struct Bird { public double U, V, Size, Phase; }
    private struct Band { public double U, V, Width, Height, Speed, Alpha; }

    private readonly Random _random = new();
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };

    private Weather _weather = Weather.Off;
    private DayPart _part;
    private double _time;
    private bool _seeded;

    private BitmapSource[] _cloudShapes = [];
    private Cloud[] _clouds = [];
    private RainDrop[] _drops = [];
    private Flake[] _flakes = [];
    private Star[] _stars = [];
    private Band[] _fog = [];
    private Bird[] _birds = [];
    private double _birdSpeed, _nextFlock;
    private double _shootingAge = 99, _nextShooting;
    private Point _shootingFrom, _shootingDir;
    private double _flashAge = 99, _nextFlash;
    private Point[] _bolt = [];

    private double _cloudOpacity;
    private Brush? _veil, _dim, _glow, _rays, _fogBrush;
    private Pen? _rainFar, _rainNear, _birdPen;
    private Brush[] _flakeShades = [];

    public WeatherOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
        _timer.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) => UpdateRunning();
        Loaded += (_, _) => UpdateRunning();
        Unloaded += (_, _) => _timer.Stop();
    }

    private bool Active => _weather is not (Weather.Off or Weather.Random);

    private void Reset()
    {
        _weather = Scene?.Weather ?? Weather.Off;
        _part = Scene?.Part ?? DayPart.Afternoon;
        _seeded = false;
        UpdateRunning();
    }

    private void UpdateRunning()
    {
        if (IsVisible && Active && SystemParameters.ClientAreaAnimation) { _clock.Restart(); _timer.Start(); }
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

    // ===== Setting up a scene =====

    private void Seed()
    {
        _seeded = true;
        _time = 0;
        if (!Active || ActualWidth <= 0 || ActualHeight <= 0) return;

        var w = _weather;
        bool overcast = w is Weather.Cloudy or Weather.Rain or Weather.Storm or Weather.Snow or Weather.Fog;

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
            Weather.Rain => (0.62, 0.7),
            Weather.Storm => (0.72, 0.48),
            _ => (0.0, 1.0)
        };
        var greyTone = _part == DayPart.Night ? Hex("#3A3F4E") : Hex("#A9AFB8");
        lit = Darken(Mix(lit, greyTone, grey), dark);
        shade = Darken(Mix(shade, greyTone, grey * 0.6), dark);
        _cloudShapes = Enumerable.Range(0, 5).Select(_ => MakeCloud(lit, shade)).ToArray();

        _cloudOpacity = w switch { Weather.Clear => 0.5, Weather.PartlyCloudy => 0.85, Weather.Fog => 0.5, _ => 0.95 };
        var count = w switch
        {
            Weather.Clear => 3, Weather.PartlyCloudy => 7, Weather.Cloudy => 18, Weather.Rain => 20,
            Weather.Storm => 22, Weather.Snow => 16, _ => 5
        };
        var wind = w == Weather.Storm ? 2.2 : 1.0;
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
                Speed = (0.004 + _random.NextDouble() * 0.008) * wind,
                Shape = _random.Next(5)
            };
        }).OrderBy(c => c.Size).ToArray();

        // A grey sky over the picture's own sky for cloudy weather. It thins out over the buildings so they stay readable.
        var veilAlpha = w switch { Weather.Cloudy => 0.62, Weather.Fog => 0.7, Weather.Snow => 0.7, Weather.Rain => 0.8, Weather.Storm => 0.88, _ => 0.0 };
        var veilColour = w == Weather.Fog
            ? _part == DayPart.Night ? Hex("#2E3442") : Hex("#C9CDD3")
            : _part switch
            {
                DayPart.Morning => Hex("#A29DA7"), DayPart.Afternoon => Hex("#949BA5"),
                DayPart.Evening => Hex("#4D4058"), _ => Hex("#0D111B")
            };
        if (w == Weather.Storm) veilColour = Darken(veilColour, 0.62);
        _veil = veilAlpha > 0 ? Freeze(new LinearGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(veilColour, veilAlpha), 0),
            new(WithAlpha(veilColour, veilAlpha * 0.95), 0.42),
            new(WithAlpha(veilColour, veilAlpha * (w == Weather.Fog ? 0.75 : 0.45)), Horizon - 0.05),
            new(WithAlpha(veilColour, veilAlpha * (w == Weather.Fog ? 0.6 : 0.25)), Horizon + 0.03),
            new(WithAlpha(veilColour, veilAlpha * (w == Weather.Fog ? 0.45 : 0.18)), 1)
        }, 90)) : null;
        var dimAlpha = w switch { Weather.Storm => 0.22, Weather.Rain => 0.1, _ => 0.0 };
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

        // Night sky: stars that twinkle, kept away from the moon.
        var sun = Sun(_part);
        _stars = _part == DayPart.Night && w is Weather.Clear or Weather.PartlyCloudy
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

        // Rain.
        var drops = w switch { Weather.Rain => 300, Weather.Storm => 460, _ => 0 };
        _drops = Enumerable.Range(0, drops).Select(_ => NewDrop(true)).ToArray();
        var rain = _part == DayPart.Night ? Hex("#8EA3C4") : Hex("#D2E2F2");
        _rainFar = Freeze(new Pen(new SolidColorBrush(WithAlpha(rain, 0.22)), 0.9));
        _rainNear = Freeze(new Pen(new SolidColorBrush(WithAlpha(rain, 0.42)), 1.3));
        _flashAge = 99;
        _nextFlash = 1.5 + _random.NextDouble() * 4;

        // Snow.
        _flakes = w == Weather.Snow
            ? Enumerable.Range(0, 190).Select(_ =>
              {
                  var depth = _random.NextDouble();
                  return new Flake
                  {
                      X = _random.NextDouble() * ActualWidth, Y = _random.NextDouble() * ActualHeight,
                      Size = 0.9 + depth * 2.0, Speed = 26 + depth * 70, Sway = 8 + _random.NextDouble() * 20, Phase = _random.NextDouble() * 6.28
                  };
              }).ToArray()
            : [];
        _flakeShades = [Freeze(new SolidColorBrush(WithAlpha(Colors.White, 0.5))), Freeze(new SolidColorBrush(WithAlpha(Colors.White, 0.75))), Freeze(new SolidColorBrush(WithAlpha(Colors.White, 0.95)))];

        // Fog banks rolling low over the river.
        _fog = w == Weather.Fog
            ? Enumerable.Range(0, 7).Select(i => new Band
              {
                  U = _random.NextDouble() * 1.4 - 0.4, V = 0.5 + i * 0.065 + _random.NextDouble() * 0.04,
                  Width = 0.8 + _random.NextDouble() * 0.7, Height = 0.12 + _random.NextDouble() * 0.1,
                  Speed = 0.003 + _random.NextDouble() * 0.006, Alpha = 0.55 + _random.NextDouble() * 0.35
              }).ToArray()
            : [];
        _fogBrush = Freeze(new RadialGradientBrush(new GradientStopCollection
        {
            new(WithAlpha(_part == DayPart.Night ? Hex("#5A6378") : Hex("#E6E9EE"), 0.7), 0),
            new(WithAlpha(_part == DayPart.Night ? Hex("#5A6378") : Hex("#E6E9EE"), 0.4), 0.5),
            new(WithAlpha(_part == DayPart.Night ? Hex("#5A6378") : Hex("#E6E9EE"), 0), 1)
        }));
    }

    private RainDrop NewDrop(bool anywhere)
    {
        var near = _random.NextDouble() < 0.35;
        var storm = _weather == Weather.Storm;
        return new RainDrop
        {
            X = _random.NextDouble() * (ActualWidth + 200) - 200,
            Y = anywhere ? _random.NextDouble() * ActualHeight : -40 - _random.NextDouble() * 60,
            Speed = (near ? 1150 : 820) + _random.NextDouble() * 300 + (storm ? 250 : 0),
            Length = (near ? 22 : 13) + _random.NextDouble() * 10,
            Near = near
        };
    }

    // ===== Moving things along =====

    private double Slant => _weather == Weather.Storm ? 0.32 : 0.18;

    private void Step(double seconds)
    {
        _time += seconds;
        double w = ActualWidth, h = ActualHeight;

        for (var i = 0; i < _clouds.Length; i++)
        {
            ref var c = ref _clouds[i];
            c.U += c.Speed * seconds;
            if (c.U > 1.05) { c.U = -c.Size - _random.NextDouble() * 0.2; c.Shape = _random.Next(_cloudShapes.Length); }
        }

        for (var i = 0; i < _drops.Length; i++)
        {
            ref var d = ref _drops[i];
            d.Y += d.Speed * seconds;
            d.X += d.Speed * Slant * seconds;
            if (d.Y > h + d.Length || d.X > w + 40) d = NewDrop(false);
        }

        for (var i = 0; i < _flakes.Length; i++)
        {
            ref var f = ref _flakes[i];
            f.Y += f.Speed * seconds;
            f.Phase += seconds;
            f.X += (Math.Sin(f.Phase * 0.9) * f.Sway + 10) * seconds;
            if (f.Y > h + f.Size) { f.Y = -f.Size; f.X = _random.NextDouble() * w; }
            if (f.X > w + f.Size) f.X = -f.Size;
        }

        for (var i = 0; i < _fog.Length; i++)
        {
            ref var b = ref _fog[i];
            b.U += b.Speed * seconds;
            if (b.U > 1.1) b.U = -b.Width - 0.1;
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

        // Lightning in storms.
        _flashAge += seconds;
        if (_weather == Weather.Storm && (_nextFlash -= seconds) <= 0)
        {
            _flashAge = 0;
            _bolt = MakeBolt();
            _nextFlash = 4 + _random.NextDouble() * 8;
        }
    }

    private Point[] MakeBolt()
    {
        var points = new List<Point>();
        var u = 0.25 + _random.NextDouble() * 0.7;
        var v = -0.02;
        points.Add(new Point(u, v));
        while (v < 0.62)
        {
            v += 0.035 + _random.NextDouble() * 0.05;
            u += (_random.NextDouble() - 0.5) * 0.05;
            points.Add(new Point(u, v));
        }
        return points.ToArray();
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

        if (_dim is not null) dc.DrawRectangle(_dim, null, bounds);
        if (_veil is not null) dc.DrawRectangle(_veil, null, pic);

        DrawStars(dc, pic);
        DrawSun(dc, pic);
        DrawBirds(dc, pic);
        DrawClouds(dc, pic);
        DrawFog(dc, pic);
        DrawLightning(dc, pic, bounds);
        DrawRain(dc);
        DrawSnow(dc);

        dc.Pop();
    }

    private void DrawSun(DrawingContext dc, Rect pic)
    {
        if (_weather is not (Weather.Clear or Weather.PartlyCloudy)) return;
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

        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(flash * 75), 225, 232, 255)), null, bounds);
        if (_flashAge > 0.35 || _bolt.Length < 2) return;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(pic, _bolt[0].X, _bolt[0].Y), false, false);
            foreach (var point in _bolt.Skip(1)) g.LineTo(At(pic, point.X, point.Y), true, true);
            // One short branch off the middle.
            var mid = _bolt[_bolt.Length / 2];
            g.BeginFigure(At(pic, mid.X, mid.Y), false, false);
            g.LineTo(At(pic, mid.X + 0.04, mid.Y + 0.07), true, true);
            g.LineTo(At(pic, mid.X + 0.06, mid.Y + 0.12), true, true);
        }
        geometry.Freeze();
        dc.PushOpacity(Math.Min(1, flash * 1.3));
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(70, 180, 200, 255)), 9) { LineJoin = PenLineJoin.Round }, geometry);
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

    private void DrawSnow(DrawingContext dc)
    {
        foreach (var f in _flakes)
            dc.DrawEllipse(_flakeShades[f.Size < 1.5 ? 0 : f.Size < 2.3 ? 1 : 2], null, new Point(f.X, f.Y), f.Size, f.Size);
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
