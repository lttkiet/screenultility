using System.Runtime.InteropServices;
using System.Windows.Threading;
using ScreenUtility.Interop;
using ScreenUtility.Models;

namespace ScreenUtility.Tray;

/// <summary>
/// Detects mouse-wheel input over the tray icon. The helper timer refreshes the icon
/// rectangle because the taskbar can move or the icon can be promoted out of overflow.
/// </summary>
public sealed class TrayScrollWatcher : IDisposable
{
    private readonly Func<PixelRect?> _rectProvider;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _rectTimer;
    private readonly NativeMethods.LowLevelMouseProc _hookProc;
    private PixelRect? _rect;
    private nint _hook;
    private bool _scrollEnabled;
    private bool _disposed;

    public event EventHandler<int>? WheelOverTray;
    public event EventHandler<System.Drawing.Point>? GlobalMouseDown;

    public TrayScrollWatcher(Func<PixelRect?> rectProvider, Dispatcher dispatcher)
    {
        _rectProvider = rectProvider;
        _dispatcher = dispatcher;
        _hookProc = HookCallback;
        _rectTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _rectTimer.Tick += (_, _) => RefreshRect();

        // Install the low-level mouse hook to monitor global mouse events (tray scrolling and outside clicks)
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(null),
            0);
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        _scrollEnabled = enabled;
        if (enabled)
        {
            RefreshRect();
            _rectTimer.Start();
        }
        else
        {
            _rectTimer.Stop();
        }
    }

    public void RefreshRect()
    {
        try
        {
            _rect = _rectProvider();
        }
        catch
        {
            _rect = null;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = (int)wParam;
                if (msg == NativeMethods.WM_LBUTTONDOWN ||
                    msg == NativeMethods.WM_RBUTTONDOWN ||
                    msg == NativeMethods.WM_MBUTTONDOWN ||
                    msg == NativeMethods.WM_NCLBUTTONDOWN ||
                    msg == NativeMethods.WM_NCRBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    var pt = new System.Drawing.Point(info.pt.X, info.pt.Y);
                    _dispatcher.BeginInvoke(DispatcherPriority.Input,
                        new Action(() => GlobalMouseDown?.Invoke(this, pt)));
                }
                else if (msg == NativeMethods.WM_MOUSEWHEEL && _scrollEnabled && _rect is { } rect)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    if (rect.Contains(info.pt.X, info.pt.Y))
                    {
                        var delta = (short)((info.mouseData >> 16) & 0xFFFF);
                        var notches = delta / 120;
                        if (notches == 0)
                        {
                            notches = Math.Sign(delta);
                        }

                        if (notches != 0)
                        {
                            _dispatcher.BeginInvoke(DispatcherPriority.Input,
                                new Action(() => WheelOverTray?.Invoke(this, notches)));
                        }
                    }
                }
            }
        }
        catch
        {
            // Never let a hook callback throw into user32.
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rectTimer.Stop();
        if (_hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }

        _scrollEnabled = false;
    }
}
