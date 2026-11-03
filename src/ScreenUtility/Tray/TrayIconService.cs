using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ScreenUtility.Interop;
using ScreenUtility.Models;

namespace ScreenUtility.Tray;

/// <summary>
/// Custom Shell_NotifyIcon host. Using the shell API directly gives us the tray icon
/// rectangle, which is needed for wheel-over-tray and flyout anchoring.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private static readonly Guid TrayGuid = new("5f0f6c0e-6f5d-4f9f-bf8e-1a4b5c6d7e8f");

    private readonly HwndSource _source;
    private readonly nint _callbackMessage;
    private Icon? _icon;
    private bool _disposed;
    private bool _darkIcon;
    private bool _useGuid = true;
    private long _lastClickTick;
    private long _lastContextTick;

    public event EventHandler? LeftClick;
    public event EventHandler<Point>? ContextMenuRequested;

    public nint Handle => _source.Handle;

    public TrayIconService(bool darkTaskbar)
    {
        _callbackMessage = new nint(NativeMethods.WM_APP + 1);
        _source = new HwndSource(new HwndSourceParameters("ScreenUtilityTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _source.AddHook(WndProc);

        _darkIcon = darkTaskbar;
        LoadIcon(darkTaskbar);

        var data = CreateData();
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP |
                      NativeMethods.NIF_GUID | NativeMethods.NIF_SHOWTIP;
        data.hIcon = _icon?.Handle ?? 0;
        data.uCallbackMessage = (uint)_callbackMessage;
        data.szTip = "Screen Utility";
        data.guidItem = TrayGuid;

        // Clean up any stale registration left by previous abnormal process termination
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            // If GUID registration fails, retry with standard HWND/uID registration
            _useGuid = false;
            data = CreateData();
            data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
            data.hIcon = _icon?.Handle ?? 0;
            data.uCallbackMessage = (uint)_callbackMessage;
            data.szTip = "Screen Utility";
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);

            if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Could not create the tray icon. Win32Error: {err}");
            }
        }

        data.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
    }

    public void SetTaskbarTheme(bool darkTaskbar)
    {
        if (_disposed || _darkIcon == darkTaskbar)
        {
            return;
        }

        _darkIcon = darkTaskbar;
        LoadIcon(darkTaskbar);

        var data = CreateData();
        data.uFlags = NativeMethods.NIF_ICON;
        data.hIcon = _icon?.Handle ?? 0;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    public void SetTooltip(string text)
    {
        if (_disposed)
        {
            return;
        }

        var data = CreateData();
        data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        data.szTip = text.Length > 127 ? text[..127] : text;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    public PixelRect? GetIconRect()
    {
        if (_disposed)
        {
            return null;
        }

        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
            hWnd = _source.Handle,
            uID = _useGuid ? 0u : 1u,
            guidItem = _useGuid ? TrayGuid : Guid.Empty
        };

        if (NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect) != 0)
        {
            return null;
        }

        return new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private void LoadIcon(bool darkTaskbar)
    {
        var file = darkTaskbar ? "tray-dark.ico" : "tray-light.ico";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", file);
        var width = Math.Max(16, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON));
        var height = Math.Max(16, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON));

        var previous = _icon;
        try
        {
            _icon = new Icon(path, new Size(width, height));
            previous?.Dispose();
        }
        catch
        {
            _icon = previous;
        }
    }

    private NativeMethods.NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _source.Handle,
        uID = _useGuid ? 0u : 1u,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
        guidItem = _useGuid ? TrayGuid : Guid.Empty
    };

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != (int)_callbackMessage)
        {
            return 0;
        }

        var x = NativeMethods.GetXParam(wParam);
        var y = NativeMethods.GetYParam(wParam);
        var message = NativeMethods.LoWord((int)lParam);

        switch (message)
        {
            case NativeMethods.NIN_SELECT:
            case NativeMethods.NIN_KEYSELECT:
            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_LBUTTONDBLCLK:
                if (Environment.TickCount64 - _lastClickTick > 180)
                {
                    _lastClickTick = Environment.TickCount64;
                    LeftClick?.Invoke(this, EventArgs.Empty);
                }

                handled = true;
                break;

            case NativeMethods.WM_CONTEXTMENU:
            case NativeMethods.WM_RBUTTONUP:
                LeftClickCancelled();
                RequestContextMenu(x, y);
                handled = true;
                break;
        }

        return 0;
    }

    private void LeftClickCancelled()
    {
        // Keep context-menu clicks from being interpreted as a flyout toggle.
        _lastClickTick = Environment.TickCount64;
    }

    private void RequestContextMenu(int x, int y)
    {
        if (Environment.TickCount64 - _lastContextTick <= 250)
        {
            return;
        }

        _lastContextTick = Environment.TickCount64;
        if (x == 0 && y == 0)
        {
            NativeMethods.GetCursorPos(out var cursor);
            x = cursor.X;
            y = cursor.Y;
        }

        ContextMenuRequested?.Invoke(this, new Point(x, y));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var data = CreateData();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _icon?.Dispose();
        _icon = null;
    }
}
