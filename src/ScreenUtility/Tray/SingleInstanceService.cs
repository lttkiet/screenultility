using System.Threading;
using System.Windows.Interop;
using ScreenUtility.Interop;

namespace ScreenUtility.Tray;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\ScreenUtility.SingleInstance";
    private static readonly uint ShowMessage = NativeMethods.RegisterWindowMessage("ScreenUtility.ShowFlyout");

    private readonly Mutex _mutex;
    private readonly HwndSource? _source;
    private bool _owned;

    public event EventHandler? ShowRequested;

    public SingleInstanceService()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
        _owned = createdNew;

        if (!createdNew)
        {
            return;
        }

        _source = new HwndSource(new HwndSourceParameters("ScreenUtilityInstance")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _source.AddHook(WndProc);
    }

    public bool IsFirstInstance { get; }

    public static void SignalExisting()
    {
        if (ShowMessage != 0)
        {
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowMessage, 0, 0);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == ShowMessage)
        {
            handled = true;
            ShowRequested?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }

        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore mutex ownership edge cases during shutdown.
            }
        }

        _mutex.Dispose();
        _owned = false;
    }
}
