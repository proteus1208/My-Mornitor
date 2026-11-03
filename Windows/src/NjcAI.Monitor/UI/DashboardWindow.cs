using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using WpfImage = System.Windows.Controls.Image;

namespace NjcAI.Monitor.UI;

internal sealed class DashboardWindow : Window
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;

    private readonly WpfImage _image;
    private readonly TextBlock _status;
    private bool _allowClose;

    public event Action? UsbDeviceChanged;

    public DashboardWindow()
    {
        Title = "NJC AI Monitor — Windows Preview";
        Width = 1280;
        Height = 340;
        MinWidth = 760;
        MinHeight = 220;
        Background = new SolidColorBrush(MediaColor.FromRgb(10, 15, 27));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _image = new WpfImage
        {
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _status = new TextBlock
        {
            Text = "Starting…",
            Foreground = new SolidColorBrush(MediaColor.FromRgb(150, 160, 182)),
            Background = new SolidColorBrush(MediaColor.FromRgb(18, 24, 38)),
            Padding = new Thickness(10, 5, 10, 5),
            FontFamily = new MediaFontFamily("Microsoft YaHei UI"),
            FontSize = 12,
        };
        Grid.SetRow(_image, 0);
        Grid.SetRow(_status, 1);
        grid.Children.Add(_image);
        grid.Children.Add(_status);
        Content = grid;

        Closing += (_, eventArgs) =>
        {
            if (_allowClose)
            {
                return;
            }
            eventArgs.Cancel = true;
            Hide();
        };
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WindowHook);
        };
    }

    public void SetFrame(BitmapSource source, string status)
    {
        _image.Source = source;
        _status.Text = status;
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    public static BitmapSource ToBitmapSource(DrawingBitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                handle, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            _ = DeleteObject(handle);
        }
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmDeviceChange && wParam.ToInt32() is DbtDeviceArrival or DbtDeviceRemoveComplete)
        {
            UsbDeviceChanged?.Invoke();
        }
        return IntPtr.Zero;
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
