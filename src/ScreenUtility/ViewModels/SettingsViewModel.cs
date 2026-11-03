using System.Collections.ObjectModel;
using ScreenUtility.Models;
using ScreenUtility.Services;
using ScreenUtility.Theming;
using ScreenUtility.Utils;

namespace ScreenUtility.ViewModels;

public sealed class SettingsDisplayItem : ObservableObject
{
    private readonly MonitorDescriptor _descriptor;
    private readonly Action _changed;
    private string _name;
    private bool _isVisible;
    private int _position;

    public SettingsDisplayItem(
        MonitorDescriptor descriptor,
        AppSettings settings,
        string sourceText,
        Action changed)
    {
        _descriptor = descriptor;
        _changed = changed;
        _name = settings.GetName(descriptor.Key, descriptor.FriendlyName);
        _isVisible = !settings.HiddenMonitors.Contains(descriptor.Key);
        SourceText = sourceText;
    }

    public string Key => _descriptor.Key;
    public string DeviceLabel => _descriptor.DeviceLabel;
    public string ResolutionText => _descriptor.ResolutionText;
    public bool IsPrimary => _descriptor.IsPrimary;
    public string SourceText { get; }

    public string HardwareName => _descriptor.FriendlyName;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                _changed();
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                _changed();
            }
        }
    }

    public int Position
    {
        get => _position;
        set => SetProperty(ref _position, value);
    }
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly StartupService _startup;
    private readonly ThemeService _theme;
    private readonly DisplayService _displayService;
    private bool _initializing = true;

    public SettingsViewModel(
        SettingsStore settings,
        StartupService startup,
        ThemeService theme,
        DisplayService displayService)
    {
        _settings = settings;
        _startup = startup;
        _theme = theme;
        _displayService = displayService;

        var current = settings.Current;
        _themeIndex = current.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        _scrollOnTray = current.ScrollOnTray;
        _scrollStep = current.ScrollStep;
        _invertScroll = current.InvertScroll;
        _startWithWindows = startup.IsEnabled;
        _initializing = false;
    }

    public event EventHandler? ScrollSettingsChanged;
    public event EventHandler? DisplayPreferencesChanged;

    public ObservableCollection<SettingsDisplayItem> Displays { get; } = new();

    private int _themeIndex;
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (!SetProperty(ref _themeIndex, value) || _initializing)
            {
                return;
            }

            var mode = value switch { 1 => "light", 2 => "dark", _ => "system" };
            _settings.Update(s => s.Theme = mode);
            _theme.SetMode(value switch { 1 => ThemeMode.Light, 2 => ThemeMode.Dark, _ => ThemeMode.System });
        }
    }

    private bool _scrollOnTray;
    public bool ScrollOnTray
    {
        get => _scrollOnTray;
        set
        {
            if (!SetProperty(ref _scrollOnTray, value) || _initializing)
            {
                return;
            }

            _settings.Update(s => s.ScrollOnTray = value);
            ScrollSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int _scrollStep;
    public int ScrollStep
    {
        get => _scrollStep;
        set
        {
            var clamped = Math.Clamp(value, 1, 20);
            if (!SetProperty(ref _scrollStep, clamped) || _initializing)
            {
                return;
            }

            _settings.Update(s => s.ScrollStep = clamped);
            ScrollSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _invertScroll;
    public bool InvertScroll
    {
        get => _invertScroll;
        set
        {
            if (!SetProperty(ref _invertScroll, value) || _initializing)
            {
                return;
            }

            _settings.Update(s => s.InvertScroll = value);
            ScrollSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _startWithWindows;
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetProperty(ref _startWithWindows, value) || _initializing)
            {
                return;
            }

            if (!_startup.SetEnabled(value))
            {
                _startWithWindows = !value;
                OnPropertyChanged();
                return;
            }

            _settings.Update(s => s.StartWithWindows = value);
        }
    }

    public string SettingsPath => _settings.FilePath;

    public async Task LoadDisplaysAsync(Func<string, string>? sourceLookup = null)
    {
        var descriptors = await _displayService.EnumerateAsync();
        var settings = _settings.Current;

        var order = settings.MonitorOrder
            .Select((key, index) => (key, index))
            .ToDictionary(pair => pair.key, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        var ordered = descriptors
            .OrderBy(d => order.TryGetValue(d.Key, out var index) ? index : int.MaxValue)
            .ThenBy(d => d.Number)
            .ToList();

        Displays.Clear();
        var position = 1;
        foreach (var descriptor in ordered)
        {
            var source = sourceLookup?.Invoke(descriptor.Key) ?? "Unknown";
            var item = new SettingsDisplayItem(descriptor, settings, source, OnDisplayItemChanged)
            {
                Position = position++
            };
            Displays.Add(item);
        }
    }

    private void OnDisplayItemChanged()
    {
        var current = _settings.Current;
        _settings.Update(s =>
        {
            s.MonitorNames = Displays
                .Where(d => !string.IsNullOrWhiteSpace(d.Name) && !string.Equals(d.Name, d.HardwareName, StringComparison.Ordinal))
                .ToDictionary(d => d.Key, d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase);
            s.HiddenMonitors = Displays
                .Where(d => !d.IsVisible)
                .Select(d => d.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        });

        DisplayPreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveDisplay(SettingsDisplayItem item, int delta)
    {
        var index = Displays.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Displays.Count)
        {
            return;
        }

        Displays.Move(index, target);
        for (var i = 0; i < Displays.Count; i++)
        {
            Displays[i].Position = i + 1;
        }

        var order = Displays.Select(d => d.Key).ToList();
        _settings.Update(s => s.MonitorOrder = order);
        DisplayPreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetSourceLabel(BrightnessSource source) => source switch
    {
        BrightnessSource.DdcCi => "DDC/CI",
        BrightnessSource.Wmi => "WMI",
        _ => "Not supported"
    };
}
