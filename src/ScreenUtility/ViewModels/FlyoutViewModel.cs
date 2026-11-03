using System.Collections.ObjectModel;
using ScreenUtility.Models;
using ScreenUtility.Services;
using ScreenUtility.Utils;

namespace ScreenUtility.ViewModels;

public sealed class FlyoutViewModel : ObservableObject, IDisposable
{
    private readonly DisplayService _displayService;
    private readonly WmiMonitorService _wmi;
    private readonly OrientationService _orientationService;
    private readonly SettingsStore _settings;
    private bool _isRefreshing;
    private bool _linkedBrightness;
    private bool _syncingBrightness;
    private bool _disposed;

    public FlyoutViewModel(
        DisplayService displayService,
        WmiMonitorService wmi,
        OrientationService orientationService,
        SettingsStore settings)
    {
        _displayService = displayService;
        _wmi = wmi;
        _orientationService = orientationService;
        _settings = settings;
        _linkedBrightness = settings.Current.LinkedBrightness;
    }

    public ObservableCollection<MonitorViewModel> Monitors { get; } = new();

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                OnPropertyChanged(nameof(HasMonitors));
                OnPropertyChanged(nameof(EmptyMessage));
            }
        }
    }

    public bool HasMonitors => Monitors.Count > 0;
    public bool HasBusyMonitor => Monitors.Any(m => m.IsBusy);
    public int ScrollStep => Math.Clamp(_settings.Current.ScrollStep, 1, 20);
    public bool InvertScroll => _settings.Current.InvertScroll;

    public string EmptyMessage => IsRefreshing
        ? "Detecting displays..."
        : _settings.Current.HiddenMonitors.Count > 0
            ? "All displays are hidden. Turn one back on in Settings."
            : "No compatible displays found.";

    public bool LinkedBrightness
    {
        get => _linkedBrightness;
        set
        {
            if (!SetProperty(ref _linkedBrightness, value))
            {
                return;
            }

            _settings.Update(s => s.LinkedBrightness = value);
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed || IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var descriptors = await _displayService.EnumerateAsync();
            if (_disposed)
            {
                return;
            }

            var settings = _settings.Current;
            var ordered = ApplyOrder(descriptors, settings);

            foreach (var monitor in Monitors)
            {
                monitor.BrightnessChanged -= OnMonitorBrightnessChanged;
                monitor.Dispose();
            }

            Monitors.Clear();

            foreach (var descriptor in ordered)
            {
                if (settings.HiddenMonitors.Contains(descriptor.Key))
                {
                    continue;
                }

                var controller = new MonitorController(descriptor, _wmi, _orientationService);
                var name = settings.GetName(descriptor.Key, descriptor.FriendlyName);
                var vm = new MonitorViewModel(descriptor, controller, name, descriptor.Number);
                vm.BrightnessChanged += OnMonitorBrightnessChanged;
                Monitors.Add(vm);
            }

            OnPropertyChanged(nameof(HasMonitors));
            OnPropertyChanged(nameof(EmptyMessage));

            using var throttle = new SemaphoreSlim(2, 2);
            var probes = Monitors.Select(async monitor =>
            {
                await throttle.WaitAsync();
                try
                {
                    await monitor.ProbeAsync();
                }
                finally
                {
                    throttle.Release();
                }
            });

            await Task.WhenAll(probes);
        }
        finally
        {
            IsRefreshing = false;
            OnPropertyChanged(nameof(HasMonitors));
            OnPropertyChanged(nameof(EmptyMessage));
        }
    }

    private static IEnumerable<MonitorDescriptor> ApplyOrder(
        IReadOnlyList<MonitorDescriptor> descriptors,
        AppSettings settings)
    {
        if (settings.MonitorOrder.Count == 0)
        {
            return descriptors;
        }

        var order = settings.MonitorOrder
            .Select((key, index) => (key, index))
            .ToDictionary(pair => pair.key, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        return descriptors
            .OrderBy(d => order.TryGetValue(d.Key, out var i) ? i : int.MaxValue)
            .ThenBy(d => d.Number);
    }

    private void OnMonitorBrightnessChanged(object? sender, double value)
    {
        if (!LinkedBrightness || _syncingBrightness)
        {
            return;
        }

        _syncingBrightness = true;
        try
        {
            foreach (var monitor in Monitors)
            {
                if (!ReferenceEquals(monitor, sender) && monitor.SupportsBrightness)
                {
                    monitor.SetBrightnessFromLink(value);
                }
            }
        }
        finally
        {
            _syncingBrightness = false;
        }
    }

    public void AdjustAllBrightness(int delta)
    {
        _syncingBrightness = true;
        try
        {
            foreach (var monitor in Monitors)
            {
                if (monitor.SupportsBrightness)
                {
                    monitor.Brightness = Math.Clamp(monitor.Brightness + delta, 0, 100);
                }
            }
        }
        finally
        {
            _syncingBrightness = false;
        }
    }

    public void ApplyDisplayPreferences()
    {
        var settings = _settings.Current;
        foreach (var monitor in Monitors)
        {
            monitor.UpdateDisplayNameFromSettings(settings.GetName(monitor.Key, monitor.Descriptor.FriendlyName));
        }

        _linkedBrightness = settings.LinkedBrightness;
        OnPropertyChanged(nameof(LinkedBrightness));
        OnPropertyChanged(nameof(ScrollStep));
        OnPropertyChanged(nameof(InvertScroll));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var monitor in Monitors)
        {
            monitor.BrightnessChanged -= OnMonitorBrightnessChanged;
            monitor.Dispose();
        }

        Monitors.Clear();
    }
}
