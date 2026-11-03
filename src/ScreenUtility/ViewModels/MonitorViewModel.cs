using ScreenUtility.Models;
using ScreenUtility.Services;
using ScreenUtility.Utils;

namespace ScreenUtility.ViewModels;

public sealed class MonitorViewModel : ObservableObject, IDisposable
{
    private readonly IMonitorController _controller;
    private readonly DebouncedAction<int> _brightnessDebounce;
    private readonly DebouncedAction<int> _contrastDebounce;
    private readonly int _number;

    private MonitorCapabilities _capabilities = new();
    private double _brightness;
    private double _contrast;
    private DisplayOrientation _orientation;
    private string _displayName;
    private string? _statusMessage;
    private bool _isReady;
    private bool _isApplyingOrientation;
    private bool _suppressLinkEvent;
    private bool _disposed;

    public MonitorViewModel(MonitorDescriptor descriptor, IMonitorController controller, string displayName, int number)
    {
        Descriptor = descriptor;
        _controller = controller;
        _displayName = displayName;
        _number = number;
        _brightnessDebounce = new DebouncedAction<int>(TimeSpan.FromMilliseconds(200), WriteBrightnessAsync);
        _contrastDebounce = new DebouncedAction<int>(TimeSpan.FromMilliseconds(200), WriteContrastAsync);
    }

    public event EventHandler<double>? BrightnessChanged;

    public MonitorDescriptor Descriptor { get; }
    public string Key => Descriptor.Key;
    public int Number => _number;
    public bool IsPrimary => Descriptor.IsPrimary;
    public string ResolutionText => Descriptor.ResolutionText;
    public string DeviceLabel => Descriptor.DeviceLabel;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                OnPropertyChanged(nameof(DisplayNameWithNumber));
            }
        }
    }

    public string DisplayNameWithNumber => $"Display {Number}";

    public MonitorCapabilities Capabilities => _capabilities;

    public bool SupportsBrightness => _capabilities.HasBrightness;
    public bool SupportsContrast => _capabilities.HasContrast;
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsReady => _isReady;
    public bool IsBusy => _isApplyingOrientation;

    public string BrightnessSourceLabel => _capabilities.BrightnessSource switch
    {
        BrightnessSource.DdcCi => "DDC/CI",
        BrightnessSource.Wmi => "WMI",
        _ => "Not supported"
    };

    public string BrightnessGlyph => _capabilities.BrightnessSource == BrightnessSource.Wmi ? "\uE770" : "\uE7F4";
    public string MonitorGlyph => _capabilities.BrightnessSource == BrightnessSource.Wmi ? "\uE770" : "\uE7F4";

    public double Brightness
    {
        get => _brightness;
        set
        {
            var rounded = Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
            if (!SetProperty(ref _brightness, rounded))
            {
                return;
            }

            OnPropertyChanged(nameof(BrightnessPercent));
            if (!_isReady)
            {
                return;
            }

            _brightnessDebounce.Trigger((int)rounded);
            if (!_suppressLinkEvent)
            {
                BrightnessChanged?.Invoke(this, rounded);
            }
        }
    }

    public int BrightnessPercent
    {
        get => (int)Math.Round(_brightness);
        set => Brightness = Math.Clamp(value, 0, 100);
    }

    public double Contrast
    {
        get => _contrast;
        set
        {
            var rounded = Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
            if (!SetProperty(ref _contrast, rounded))
            {
                return;
            }

            OnPropertyChanged(nameof(ContrastPercent));
            if (_isReady)
            {
                _contrastDebounce.Trigger((int)rounded);
            }
        }
    }

    public int ContrastPercent
    {
        get => (int)Math.Round(_contrast);
        set => Contrast = Math.Clamp(value, 0, 100);
    }

    public DisplayOrientation SelectedOrientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value)
            {
                return;
            }

            var previous = _orientation;
            if (!SetProperty(ref _orientation, value))
            {
                return;
            }

            OnPropertyChanged(nameof(OrientationLabel));
            if (_isReady)
            {
                _ = ApplyOrientationAsync(value, previous);
            }
        }
    }

    public string OrientationLabel => _orientation.ToDisplayName();

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool IsApplyingOrientation
    {
        get => _isApplyingOrientation;
        private set
        {
            if (SetProperty(ref _isApplyingOrientation, value))
            {
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    public async Task ProbeAsync()
    {
        StatusMessage = null;
        try
        {
            var capabilities = await _controller.ProbeAsync();
            _capabilities = capabilities;

            _isReady = false;
            Brightness = capabilities.Brightness;
            Contrast = capabilities.Contrast;
            _orientation = capabilities.Orientation;
            OnPropertyChanged(nameof(SelectedOrientation));
            OnPropertyChanged(nameof(OrientationLabel));
            OnPropertyChanged(nameof(SupportsBrightness));
            OnPropertyChanged(nameof(SupportsContrast));
            OnPropertyChanged(nameof(BrightnessSourceLabel));
            OnPropertyChanged(nameof(BrightnessGlyph));
            OnPropertyChanged(nameof(MonitorGlyph));

            _isReady = true;
            OnPropertyChanged(nameof(IsReady));
        }
        catch (Exception ex)
        {
            _isReady = true;
            StatusMessage = ex.Message;
            OnPropertyChanged(nameof(IsReady));
        }
    }

    public void NudgeBrightness(int delta)
    {
        Brightness = Math.Clamp(_brightness + delta, 0, 100);
    }

    public void SetBrightnessFromLink(double value)
    {
        _suppressLinkEvent = true;
        try
        {
            Brightness = value;
        }
        finally
        {
            _suppressLinkEvent = false;
        }
    }

    public void UpdateDisplayNameFromSettings(string name) => DisplayName = name;

    private async Task WriteBrightnessAsync(int percent)
    {
        try
        {
            await _controller.SetBrightnessAsync(percent);
            if (StatusMessage is not null && StatusMessage.Contains("brightness", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = null;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task WriteContrastAsync(int percent)
    {
        try
        {
            await _controller.SetContrastAsync(percent);
            if (StatusMessage is not null && StatusMessage.Contains("contrast", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = null;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task ApplyOrientationAsync(DisplayOrientation target, DisplayOrientation previous)
    {
        IsApplyingOrientation = true;
        StatusMessage = null;

        try
        {
            var result = await _controller.SetOrientationAsync(target);
            if (result.Succeeded)
            {
                _orientation = result.Orientation;
                OnPropertyChanged(nameof(SelectedOrientation));
                OnPropertyChanged(nameof(OrientationLabel));
                if (!string.IsNullOrWhiteSpace(result.Message))
                {
                    StatusMessage = result.Message;
                }
            }
            else
            {
                _orientation = previous;
                OnPropertyChanged(nameof(SelectedOrientation));
                OnPropertyChanged(nameof(OrientationLabel));
                StatusMessage = result.Message ?? "The orientation change failed.";
            }
        }
        catch (Exception ex)
        {
            _orientation = previous;
            OnPropertyChanged(nameof(SelectedOrientation));
            OnPropertyChanged(nameof(OrientationLabel));
            StatusMessage = ex.Message;
        }
        finally
        {
            IsApplyingOrientation = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _brightnessDebounce.Dispose();
        _contrastDebounce.Dispose();
        _controller.Dispose();
    }
}
