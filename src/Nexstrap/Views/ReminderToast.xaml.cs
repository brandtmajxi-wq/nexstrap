using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Nexstrap.Views;

/// <summary>Sits in the corner for a few seconds. Clicks pass through it and it never takes focus, so a game carries on undisturbed.</summary>
public partial class ReminderToast : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x8000000;

    private static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(8);

    private ReminderToast(string title, string text)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = text;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, GwlExStyle, GetWindowLongPtr(handle, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate);
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight;
        };
    }

    public static void Pop(string title, string text)
    {
        var toast = new ReminderToast(title, text);
        var timer = new DispatcherTimer { Interval = ShownFor };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            toast.Close();
        };
        toast.Show();
        timer.Start();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
