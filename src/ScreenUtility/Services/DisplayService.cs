using System.Runtime.InteropServices;
using ScreenUtility.Interop;
using ScreenUtility.Models;

namespace ScreenUtility.Services;

public sealed class DisplayService
{
    private readonly WmiMonitorService _wmi;

    public DisplayService(WmiMonitorService wmi)
    {
        _wmi = wmi;
    }

    public async Task<IReadOnlyList<MonitorDescriptor>> EnumerateAsync()
    {
        await _wmi.EnsureLoadedAsync(force: true);
        return await Task.Run(EnumerateCore).ConfigureAwait(false);
    }

    private List<MonitorDescriptor> EnumerateCore()
    {
        var monitors = new List<(nint Handle, NativeMethods.MONITORINFOEX Info)>();

        NativeMethods.MonitorEnumProc callback = (nint hMonitor, nint hdc, ref NativeMethods.RECT clip, nint data) =>
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = string.Empty
            };

            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add((hMonitor, info));
            }

            return true;
        };

        NativeMethods.EnumDisplayMonitors(0, 0, callback, 0);

        var result = new List<MonitorDescriptor>(monitors.Count);
        foreach (var (handle, info) in monitors)
        {
            var monitorId = GetMonitorDeviceId(info.szDevice);
            var pnp = WmiMonitorService.ExtractPnpCode(monitorId);
            var friendly = _wmi.GetFriendlyName(monitorId, pnp);

            if (string.IsNullOrWhiteSpace(friendly) || WmiMonitorService.IsGeneric(friendly))
            {
                var eddName = GetMonitorFriendlyName(info.szDevice);
                if (!string.IsNullOrWhiteSpace(eddName) && !WmiMonitorService.IsGeneric(eddName))
                {
                    friendly = eddName;
                }
            }

            if (string.IsNullOrWhiteSpace(friendly) || WmiMonitorService.IsGeneric(friendly))
            {
                if (_wmi.IsInternalPanel(monitorId, pnp))
                {
                    friendly = "Built-in display";
                }
                else
                {
                    friendly = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0 ? "Primary display" : "Display";
                }
            }

            result.Add(new MonitorDescriptor
            {
                Key = string.IsNullOrWhiteSpace(monitorId) ? info.szDevice : monitorId!,
                DeviceName = info.szDevice,
                FriendlyName = friendly!,
                MonitorId = monitorId,
                PnpCode = pnp,
                Bounds = new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
                WorkArea = new PixelRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
                IsPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                Handle = handle
            });
        }

        var ordered = result
            .OrderBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i] = CloneWithNumber(ordered[i], i + 1);
        }

        return ordered;
    }

    private static MonitorDescriptor CloneWithNumber(MonitorDescriptor descriptor, int number) => new()
    {
        Key = descriptor.Key,
        DeviceName = descriptor.DeviceName,
        FriendlyName = descriptor.FriendlyName,
        MonitorId = descriptor.MonitorId,
        PnpCode = descriptor.PnpCode,
        Bounds = descriptor.Bounds,
        WorkArea = descriptor.WorkArea,
        IsPrimary = descriptor.IsPrimary,
        Number = number,
        Handle = descriptor.Handle
    };

    private static string? GetMonitorDeviceId(string deviceName)
    {
        var device = new NativeMethods.DISPLAY_DEVICE
        {
            cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceID = string.Empty,
            DeviceKey = string.Empty
        };

        return NativeMethods.EnumDisplayDevices(deviceName, 0, ref device, NativeMethods.EDD_GET_DEVICE_ID) &&
               !string.IsNullOrWhiteSpace(device.DeviceID)
            ? device.DeviceID
            : null;
    }

    private static string? GetMonitorFriendlyName(string deviceName)
    {
        var device = new NativeMethods.DISPLAY_DEVICE
        {
            cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceID = string.Empty,
            DeviceKey = string.Empty
        };

        return NativeMethods.EnumDisplayDevices(deviceName, 0, ref device, 0) &&
               !string.IsNullOrWhiteSpace(device.DeviceString)
            ? device.DeviceString.Trim()
            : null;
    }
}
