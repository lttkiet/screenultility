using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenUtility.Interop;
using ScreenUtility.Models;
using ScreenUtility.Tray;

namespace ScreenUtility.Windows;

public sealed record OverlayRow(string Name, string Value);

public partial class OverlayWindow : Window
{
    private const int ShadowPaddingDip = 8;

    private readonly DispatcherTimer _hideTimer;
    private PixelRect _trayRect;

    public OverlayWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        _hideTimer.Tick += (_, _) => HideOverlay();
        SourceInitialized += OnSourceInitialized;
    }

    public void ShowBrightness(PixelRect trayRect, IReadOnlyList<OverlayRow> rows)
    {
        _trayRect = trayRect;
        Rows.ItemsSource = rows;
        Opacity = 0;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        Reposition();
        Opacity = 1;

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void HideOverlay()
    {
        _hideTimer.Stop();
        Hide();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new nint(style));
    }

    private void Reposition()
    {
        if (!IsVisible || _trayRect.Width <= 0)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
        {
            return;
        }

        var point = new NativeMethods.POINT { X = _trayRect.CenterX, Y = _trayRect.CenterY };
        var monitorHandle = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            szDevice = string.Empty
        };

        if (monitorHandle == 0 || !NativeMethods.GetMonitorInfo(monitorHandle, ref info))
        {
            return;
        }

        var monitor = new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
        var work = new PixelRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);

        var scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(monitorHandle, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
        {
            scale = dpiX / 96.0;
        }

        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var placement = WindowPlacement.PlaceOverlay(_trayRect, monitor, work, width, height);

        var padding = (int)Math.Round(ShadowPaddingDip * scale);
        var edge = WindowPlacement.GetTaskbarEdge(monitor, work, _trayRect);
        var offsetX = edge switch
        {
            TaskbarEdge.Left => -padding,
            TaskbarEdge.Right => padding,
            _ => 0
        };
        var offsetY = edge switch
        {
            TaskbarEdge.Top => -padding,
            TaskbarEdge.Bottom => padding,
            _ => 0
        };

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            placement.Left + offsetX,
            placement.Top + offsetY,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}
