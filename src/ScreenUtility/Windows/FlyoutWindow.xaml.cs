using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ScreenUtility.Interop;
using ScreenUtility.Models;
using ScreenUtility.Tray;
using ScreenUtility.ViewModels;

namespace ScreenUtility.Windows;

public partial class FlyoutWindow : Window
{
    private const int ShadowPaddingDip = 10;

    private readonly FlyoutViewModel _viewModel;
    private PixelRect _trayRect;
    private bool _allowClose;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? IdentifyRequested;

    public async Task OpenAsync(PixelRect trayRect)
    {
        _trayRect = trayRect;
        Opacity = 0;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        Reposition();
        Opacity = 1;
        Activate();

        await _viewModel.RefreshAsync();
        UpdateLayout();
        Reposition();
    }

    public void HideFlyout() => Hide();

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private long _lastDeactivatedTick;
    public bool JustDeactivated => Environment.TickCount64 - _lastDeactivatedTick < 250;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _lastDeactivatedTick = Environment.TickCount64;
        if (IsVisible && !_allowClose)
        {
            Hide();
        }
    }

    private void Reposition()
    {
        if (!IsVisible || _trayRect.Width <= 0 || _trayRect.Height <= 0)
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
        var placement = WindowPlacement.PlaceFlyout(_trayRect, monitor, work, width, height);

        var padding = (int)Math.Round(ShadowPaddingDip * scale);
        var edge = WindowPlacement.GetTaskbarEdge(monitor, work, _trayRect);
        var offsetX = edge == TaskbarEdge.Left ? -padding : padding;
        var offsetY = edge == TaskbarEdge.Top ? -padding : padding;

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            placement.Left + offsetX,
            placement.Top + offsetY,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync();
        UpdateLayout();
        Reposition();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnIdentifyClick(object sender, RoutedEventArgs e) => IdentifyRequested?.Invoke(this, EventArgs.Empty);
}
