using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexstrap.Controls;

/// <summary>
/// Light snow drawn over the window. It only moves while the window is in front,
/// so it costs nothing during a game, and clicks pass straight through it.
/// </summary>
public sealed class SnowOverlay : FrameworkElement
{
    private const int FlakeCount = 90;

    internal struct Flake
    {
        public double X, Y, Size, Speed, Sway, Phase;
    }

    private static readonly Brush[] Shades = [Shade(0x60), Shade(0x90), Shade(0xC8)];

    private readonly Flake[] _flakes = new Flake[FlakeCount];
    private readonly Random _random = new();
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _seeded;

    public SnowOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
        _timer.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) => UpdateRunning();
        Loaded += (_, _) =>
        {
            Nexstrap.Core.LowEndMode.Changed += OnLowEndChanged;
            UpdateRunning();
        };
        Unloaded += (_, _) =>
        {
            Nexstrap.Core.LowEndMode.Changed -= OnLowEndChanged;
            _timer.Stop();
        };
    }

    private void OnLowEndChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateRunning);

    private static Brush Shade(byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
        brush.Freeze();
        return brush;
    }

    private void UpdateRunning()
    {
        // Windows' "animation effects" switch: leave the snow still for people who turned it off.
        if (IsVisible && Nexstrap.Core.LowEndMode.Animate) { _clock.Restart(); _timer.Start(); }
        else _timer.Stop();
        InvalidateVisual();
    }

    private void Advance()
    {
        var seconds = Math.Min(_clock.Elapsed.TotalSeconds, 0.1);
        _clock.Restart();
        if (Window.GetWindow(this) is not { IsActive: true, WindowState: not WindowState.Minimized }) return;

        for (var i = 0; i < _flakes.Length; i++)
            _flakes[i] = Step(_flakes[i], seconds, ActualWidth, ActualHeight, _random);
        InvalidateVisual();
    }

    /// <summary>Moves a flake down, and puts it back at the top with a new position once it has left the window.</summary>
    internal static Flake Step(Flake flake, double seconds, double width, double height, Random random)
    {
        flake.Y += flake.Speed * seconds;
        flake.Phase += seconds;
        flake.X += Math.Sin(flake.Phase * 0.9) * flake.Sway * seconds;
        if (flake.Y > height + flake.Size)
        {
            flake.Y = -flake.Size;
            flake.X = random.NextDouble() * width;
        }
        return flake;
    }

    private void Seed()
    {
        for (var i = 0; i < _flakes.Length; i++)
        {
            var depth = _random.NextDouble();
            _flakes[i] = new Flake
            {
                X = _random.NextDouble() * ActualWidth,
                Y = _random.NextDouble() * ActualHeight,
                Size = 0.7 + depth * 1.3,
                Speed = 22 + depth * 46,
                Sway = 8 + _random.NextDouble() * 18,
                Phase = _random.NextDouble() * 6.28
            };
        }
        _seeded = true;
    }

    protected override void OnRender(DrawingContext context)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        if (!_seeded) Seed();

        foreach (var flake in _flakes)
        {
            var shade = Shades[flake.Size < 1.1 ? 0 : flake.Size < 1.6 ? 1 : 2];
            context.DrawEllipse(shade, null, new Point(flake.X, flake.Y), flake.Size, flake.Size);
        }
    }
}
