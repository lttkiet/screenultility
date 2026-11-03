using System.Runtime.InteropServices;
using ScreenUtility.Interop;

namespace ScreenUtility.Services;

/// <summary>
/// Owns the physical monitor handles for a single HMONITOR and releases them on dispose.
/// </summary>
internal sealed class DdcMonitor : IDisposable
{
    private NativeMethods.PHYSICAL_MONITOR[] _monitors;
    private bool _disposed;

    private DdcMonitor(NativeMethods.PHYSICAL_MONITOR[] monitors)
    {
        _monitors = monitors;
    }

    public nint Handle => _monitors.Length > 0 ? _monitors[0].hPhysicalMonitor : 0;

    public static DdcMonitor? TryOpen(nint hMonitor)
    {
        if (hMonitor == 0 ||
            !NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) ||
            count == 0)
        {
            return null;
        }

        var monitors = new NativeMethods.PHYSICAL_MONITOR[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
        {
            return null;
        }

        // Some drivers report success but return no usable handle.
        if (monitors.All(m => m.hPhysicalMonitor == 0))
        {
            NativeMethods.DestroyPhysicalMonitors(count, monitors);
            return null;
        }

        return new DdcMonitor(monitors);
    }

    public bool TryGetBrightness(out uint min, out uint current, out uint max)
    {
        min = current = max = 0;
        return !_disposed && Handle != 0 && NativeMethods.GetMonitorBrightness(Handle, out min, out current, out max);
    }

    public bool SetBrightness(uint value) =>
        !_disposed && Handle != 0 && NativeMethods.SetMonitorBrightness(Handle, value);

    public bool TryGetContrast(out uint min, out uint current, out uint max)
    {
        min = current = max = 0;
        return !_disposed && Handle != 0 && NativeMethods.GetMonitorContrast(Handle, out min, out current, out max);
    }

    public bool SetContrast(uint value) =>
        !_disposed && Handle != 0 && NativeMethods.SetMonitorContrast(Handle, value);

    ~DdcMonitor()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_monitors.Length > 0)
        {
            NativeMethods.DestroyPhysicalMonitors((uint)_monitors.Length, _monitors);
        }

        _monitors = Array.Empty<NativeMethods.PHYSICAL_MONITOR>();
    }
}
