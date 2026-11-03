using ScreenUtility.Models;
using ScreenUtility.Utils;

namespace ScreenUtility.Services;

public interface IMonitorController : IDisposable
{
    MonitorDescriptor Descriptor { get; }
    MonitorCapabilities Capabilities { get; }
    Task<MonitorCapabilities> ProbeAsync();
    Task SetBrightnessAsync(int percent);
    Task SetContrastAsync(int percent);
    Task<OrientationApplyResult> SetOrientationAsync(DisplayOrientation target);
}

public sealed class MonitorController : IMonitorController
{
    private readonly WmiMonitorService _wmi;
    private readonly OrientationService _orientation;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DdcMonitor? _ddc;
    private string? _wmiBrightnessInstance;
    private bool _disposed;

    public MonitorController(MonitorDescriptor descriptor, WmiMonitorService wmi, OrientationService orientation)
    {
        Descriptor = descriptor;
        _wmi = wmi;
        _orientation = orientation;
        Capabilities = new MonitorCapabilities();
    }

    public MonitorDescriptor Descriptor { get; }
    public MonitorCapabilities Capabilities { get; private set; }

    public async Task<MonitorCapabilities> ProbeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return Capabilities;
            }

            var (ddc, ddcBrightness, ddcContrast, orientation) = await Task.Run(() =>
            {
                var opened = DdcMonitor.TryOpen(Descriptor.Handle);
                uint bMin = 0, bCurrent = 0, bMax = 0;
                uint cMin = 0, cCurrent = 0, cMax = 0;
                var brightnessOk = opened?.TryGetBrightness(out bMin, out bCurrent, out bMax) == true && bMax > bMin;
                var contrastOk = opened?.TryGetContrast(out cMin, out cCurrent, out cMax) == true && cMax > cMin;
                var currentOrientation = _orientation.GetCurrent(Descriptor.DeviceName);
                return (opened, (Ok: brightnessOk, Min: bMin, Current: bCurrent, Max: bMax),
                    (Ok: contrastOk, Min: cMin, Current: cCurrent, Max: cMax), currentOrientation);
            }).ConfigureAwait(false);

            _ddc?.Dispose();
            _ddc = ddc;

            var brightnessSource = BrightnessSource.None;
            var brightnessMin = 0;
            var brightnessMax = 100;
            var brightness = 0;

            if (ddcBrightness.Ok)
            {
                brightnessSource = BrightnessSource.DdcCi;
                brightnessMin = (int)ddcBrightness.Min;
                brightnessMax = (int)ddcBrightness.Max;
                brightness = ValueMap.RawToPercent((int)ddcBrightness.Current, brightnessMin, brightnessMax);
            }
            else
            {
                var panel = _wmi.GetBrightnessPanel(Descriptor.MonitorId, Descriptor.PnpCode);
                if (panel is not null)
                {
                    _wmiBrightnessInstance = panel.InstanceName;
                    brightnessSource = BrightnessSource.Wmi;
                    brightnessMin = 0;
                    brightnessMax = 100;
                    brightness = ValueMap.RawToPercent(panel.Current, 0, panel.Max);
                }
            }

            var hasContrast = ddcContrast.Ok;
            var contrastMin = hasContrast ? (int)ddcContrast.Min : 0;
            var contrastMax = hasContrast ? (int)ddcContrast.Max : 100;
            var contrast = hasContrast ? ValueMap.RawToPercent((int)ddcContrast.Current, contrastMin, contrastMax) : 0;

            Capabilities = new MonitorCapabilities
            {
                BrightnessSource = brightnessSource,
                BrightnessMin = brightnessMin,
                BrightnessMax = brightnessMax,
                Brightness = brightness,
                HasContrast = hasContrast,
                ContrastMin = contrastMin,
                ContrastMax = contrastMax,
                Contrast = contrast,
                Orientation = orientation
            };

            return Capabilities;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetBrightnessAsync(int percent)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var clamped = ValueMap.ClampPercent(percent);
            var source = Capabilities.BrightnessSource;

            if (source == BrightnessSource.DdcCi)
            {
                var raw = (uint)ValueMap.PercentToRaw(clamped, Capabilities.BrightnessMin, Capabilities.BrightnessMax);
                var ok = await Task.Run(() => _ddc?.SetBrightness(raw) == true).ConfigureAwait(false);
                if (!ok)
                {
                    throw new InvalidOperationException("The monitor did not accept the brightness change.");
                }
            }
            else if (source == BrightnessSource.Wmi && _wmiBrightnessInstance is not null)
            {
                var ok = await _wmi.SetBrightnessAsync(_wmiBrightnessInstance, clamped).ConfigureAwait(false);
                if (!ok)
                {
                    throw new InvalidOperationException("Windows did not accept the panel brightness change.");
                }
            }
            else
            {
                throw new InvalidOperationException("Brightness control is not available for this display.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetContrastAsync(int percent)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!Capabilities.HasContrast)
            {
                throw new InvalidOperationException("Contrast control is not available for this display.");
            }

            var clamped = ValueMap.ClampPercent(percent);
            var raw = (uint)ValueMap.PercentToRaw(clamped, Capabilities.ContrastMin, Capabilities.ContrastMax);
            var ok = await Task.Run(() => _ddc?.SetContrast(raw) == true).ConfigureAwait(false);
            if (!ok)
            {
                throw new InvalidOperationException("The monitor did not accept the contrast change.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OrientationApplyResult> SetOrientationAsync(DisplayOrientation target)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(() => _orientation.Set(Descriptor.DeviceName, target)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ddc?.Dispose();
        _ddc = null;
        _gate.Dispose();
    }
}
