using System.Windows;
using System.Windows.Interop;
using ScreenUtility.Interop;
using ScreenUtility.Models;

namespace ScreenUtility.Windows;

public partial class IdentifyWindow : Window
{
    private readonly PixelRect _bounds;

    public IdentifyWindow(int number, string name, PixelRect bounds)
    {
        _bounds = bounds;
        InitializeComponent();
        NumberText.Text = number.ToString();
        NameText.Text = name;
        SourceInitialized += OnSourceInitialized;
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

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            _bounds.Left,
            _bounds.Top,
            _bounds.Width,
            _bounds.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}
