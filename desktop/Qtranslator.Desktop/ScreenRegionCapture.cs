using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;

namespace Qtranslator.Desktop;

public static class ScreenRegionCapture
{
    public static MemoryStream? CaptureWithOverlay()
    {
        var bounds = Forms.SystemInformation.VirtualScreen;
        var overlay = new RegionSelectionWindow(bounds);
        if (overlay.ShowDialog() != true || overlay.SelectedRegion is not { } region) return null;
        DwmFlush();
        Thread.Sleep(80);
        return CaptureRegion(region);
    }

    public static MemoryStream CaptureRegion(Rectangle region)
    {
        if (region.Width < 1 || region.Height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "Capture region must have a positive size.");
        }

        using var bitmap = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        return stream;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}

internal sealed class RegionSelectionWindow : Window
{
    private readonly System.Windows.Shapes.Rectangle _selection = new()
    {
        Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 2,
        Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)), Visibility = Visibility.Collapsed,
    };
    private readonly Rectangle _bounds;
    private readonly System.Windows.Controls.Canvas _canvas = new();
    private Point? _start;

    public RegionSelectionWindow(Rectangle bounds)
    {
        _bounds = bounds;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 0, 0, 0));
        Topmost = true; ShowInTaskbar = false; Cursor = System.Windows.Input.Cursors.Cross; WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _canvas; _canvas.Children.Add(_selection);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowPos(handle, new IntPtr(-1), _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, 0x0040);
        };
        MouseLeftButtonDown += OnDown; MouseMove += OnMove; MouseLeftButtonUp += OnUp;
        KeyDown += (_, args) => { if (args.Key == Key.Escape) DialogResult = false; };
    }

    public Rectangle? SelectedRegion { get; private set; }
    private void OnDown(object sender, MouseButtonEventArgs args) { _start = args.GetPosition(_canvas); _selection.Visibility = Visibility.Visible; Mouse.Capture(this); }
    private void OnMove(object sender, System.Windows.Input.MouseEventArgs args) { if (_start is { } start && args.LeftButton == MouseButtonState.Pressed) Draw(start, args.GetPosition(_canvas)); }
    private void OnUp(object sender, MouseButtonEventArgs args)
    {
        if (_start is not { } start) return;
        var end = args.GetPosition(_canvas); Mouse.Capture(null); _start = null;
        var physicalStart = PointToScreen(start);
        var physicalEnd = PointToScreen(end);
        var region = new Rectangle(
            (int)Math.Round(Math.Min(physicalStart.X, physicalEnd.X)),
            (int)Math.Round(Math.Min(physicalStart.Y, physicalEnd.Y)),
            (int)Math.Round(Math.Abs(physicalEnd.X - physicalStart.X)),
            (int)Math.Round(Math.Abs(physicalEnd.Y - physicalStart.Y)));
        if (region.Width < 3 || region.Height < 3) { DialogResult = false; return; }
        SelectedRegion = region; DialogResult = true;
    }
    private void Draw(Point start, Point end)
    {
        System.Windows.Controls.Canvas.SetLeft(_selection, Math.Min(start.X, end.X));
        System.Windows.Controls.Canvas.SetTop(_selection, Math.Min(start.Y, end.Y));
        _selection.Width = Math.Abs(end.X - start.X); _selection.Height = Math.Abs(end.Y - start.Y);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
