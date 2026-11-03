using System.Windows.Interop;
using System.Windows.Threading;
using ScreenUtility.Interop;

namespace ScreenUtility.Services;

/// <summary>
/// Watches for display topology/mode changes and coalesces the burst of notifications
/// Windows sends while a display is reconfiguring.
/// </summary>
public sealed class DisplayChangeWatcher : IDisposable
{
    private readonly HwndSource _source;
    private readonly DispatcherTimer _debounce;
    private bool _disposed;

    public event EventHandler? Changed;

    public DisplayChangeWatcher()
    {
        _source = new HwndSource(new HwndSourceParameters("ScreenUtilityDisplayWatcher")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _source.AddHook(WndProc);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED)
        {
            Schedule();
        }

        return 0;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var dispatcher = _debounce.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            Schedule();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(Schedule));
        }
    }

    private void Schedule()
    {
        if (_disposed)
        {
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounce.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
