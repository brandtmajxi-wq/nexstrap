using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Nexstrap.Core;
using Nexstrap.Models;

namespace Nexstrap.Views;

public partial class LaunchScreenWindow : Window
{
    private static readonly TimeSpan FadeLength = TimeSpan.FromMilliseconds(220);

    public LaunchScreenWindow(LaunchScreenSettings options, BitmapSource? picture, BitmapSource? logo)
    {
        InitializeComponent();

        var size = LaunchScreenLayout.WindowSize(options.WindowSize);
        Width = size.Width;
        Height = size.Height;
        var place = LaunchScreenLayout.Place(SystemParameters.WorkArea, size, options.Position);
        Left = place.X;
        Top = place.Y;

        var corner = new CornerRadius(LaunchScreenLayout.CornerRadius(options.Corners));
        Root.CornerRadius = corner;
        Picture.CornerRadius = corner;

        var background = ColorUtil.TryParseHex(options.BackgroundColor, out var bg) ? bg : Color.FromRgb(0x0E, 0x0F, 0x13);
        var accent = new SolidColorBrush(ColorUtil.TryParseHex(options.AccentColor, out var ac) ? ac : Colors.White);
        var text = new SolidColorBrush(ColorUtil.TryParseHex(options.TextColor ?? "", out var chosen) ? chosen : ColorUtil.ReadableForeground(background));

        Root.Background = new SolidColorBrush(background);

        TitleText.Text = options.Title == "Starting Roblox" ? Loc.Get("LaunchScreen.DefaultTitle") : options.Title;
        TitleText.Foreground = text;
        TitleText.FontSize = LaunchScreenLayout.TitleSize(options.TextSize);
        TitleText.MaxWidth = size.Width - 60;
        if (!string.IsNullOrWhiteSpace(options.Subtitle))
        {
            SubtitleText.Text = options.Subtitle;
            SubtitleText.Foreground = text;
            SubtitleText.MaxWidth = size.Width - 60;
            SubtitleText.Visibility = Visibility.Visible;
        }

        Spinner.Stroke = accent;
        BarFill.Background = accent;
        DotOne.Fill = DotTwo.Fill = DotThree.Fill = accent;

        if (picture is not null)
        {
            Picture.Background = new ImageBrush(picture) { Stretch = options.ImageFit == LaunchImageFit.Fit ? Stretch.Uniform : Stretch.UniformToFill };
            Picture.Opacity = Math.Clamp(options.ImageOpacity, 0.1, 1);
            if (options.ImageBlur) Picture.Effect = new BlurEffect { Radius = 14 };
        }

        var pulsing = ShowLogo(options.Logo, logo);
        StartAnimation(options.Animation, pulsing, LaunchScreenLayout.SpeedFactor(options.Speed));

        if (options.Fade)
        {
            Opacity = 0;
            Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeLength));
        }
    }

    /// <summary>Returns what the pulse animation should act on.</summary>
    private UIElement ShowLogo(LaunchLogo choice, BitmapSource? logo)
    {
        if (choice == LaunchLogo.Hidden)
        {
            Logo.Visibility = Visibility.Collapsed;
            TitleText.Margin = new Thickness(0);
            return TitleText;
        }
        if (choice == LaunchLogo.Custom && logo is not null)
        {
            Logo.Visibility = Visibility.Collapsed;
            LogoPicture.Source = logo;
            LogoPicture.Visibility = Visibility.Visible;
            return LogoPicture;
        }
        return Logo;
    }

    private void StartAnimation(LaunchAnimation animation, UIElement pulsing, double speed)
    {
        TimeSpan Length(double seconds) => TimeSpan.FromSeconds(seconds * speed);

        switch (animation)
        {
            case LaunchAnimation.Spinner:
                Spinner.Visibility = Visibility.Visible;
                SpinnerTurn.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, Length(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
                break;
            case LaunchAnimation.Bar:
                BarTrack.Visibility = Visibility.Visible;
                BarMove.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(-80, 240, Length(1.3)) { RepeatBehavior = RepeatBehavior.Forever });
                break;
            case LaunchAnimation.Pulse:
                pulsing.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, Length(0.8))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
                break;
            case LaunchAnimation.Dots:
                Dots.Visibility = Visibility.Visible;
                var step = 0;
                foreach (var dot in new[] { DotOneMove, DotTwoMove, DotThreeMove })
                {
                    dot.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -10, Length(0.36))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = Length(0.14 * step++),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });
                }
                break;
        }
    }

    /// <summary>Fades away first when that's turned on.</summary>
    public void CloseSoftly(bool fade)
    {
        if (!fade)
        {
            Close();
            return;
        }
        var fadeOut = new DoubleAnimation(0, FadeLength);
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
