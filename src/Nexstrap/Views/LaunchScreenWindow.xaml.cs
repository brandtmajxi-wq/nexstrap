using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Nexstrap.Core;
using Nexstrap.Models;

namespace Nexstrap.Views;

public partial class LaunchScreenWindow : Window
{
    public LaunchScreenWindow(LaunchScreenSettings options, BitmapSource? picture)
    {
        InitializeComponent();

        var background = ColorUtil.TryParseHex(options.BackgroundColor, out var bg) ? bg : Color.FromRgb(0x0E, 0x0F, 0x13);
        var accent = new SolidColorBrush(ColorUtil.TryParseHex(options.AccentColor, out var ac) ? ac : Colors.White);
        var text = new SolidColorBrush(ColorUtil.ReadableForeground(background));

        Root.Background = new SolidColorBrush(background);
        TitleText.Text = options.Title;
        TitleText.Foreground = text;
        Spinner.Stroke = accent;
        BarFill.Background = accent;
        if (picture is not null)
            Picture.Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };

        StartAnimation(options.Animation);
    }

    private void StartAnimation(LaunchAnimation animation)
    {
        switch (animation)
        {
            case LaunchAnimation.Spinner:
                Spinner.Visibility = Visibility.Visible;
                SpinnerTurn.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
                break;
            case LaunchAnimation.Bar:
                BarTrack.Visibility = Visibility.Visible;
                BarMove.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(-80, 240, TimeSpan.FromSeconds(1.3)) { RepeatBehavior = RepeatBehavior.Forever });
                break;
            case LaunchAnimation.Pulse:
                Logo.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.8))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
                break;
        }
    }
}
