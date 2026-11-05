using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenUtility.Interop;
using ScreenUtility.Models;
using ScreenUtility.Services;
using ScreenUtility.Theming;
using ScreenUtility.Tray;
using ScreenUtility.ViewModels;
using ScreenUtility.Windows;

namespace ScreenUtility;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private SettingsStore? _settings;
    private ThemeService? _theme;
    private StartupService? _startup;
    private WmiMonitorService? _wmi;
    private DisplayService? _displayService;
    private OrientationService? _orientationService;
    private TrayIconService? _tray;
    private TrayScrollWatcher? _scrollWatcher;
    private DisplayChangeWatcher? _displayWatcher;
    private FlyoutViewModel? _flyoutViewModel;
    private FlyoutWindow? _flyout;
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private ContextMenu? _trayMenu;
    private DispatcherTimer? _prefsRefreshTimer;
    private readonly List<IdentifyWindow> _identifyWindows = new();
    private bool _shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsFirstInstance)
        {
            SingleInstanceService.SignalExisting();
            Shutdown();
            return;
        }

        try
        {
            _settings = new SettingsStore();
            _startup = new StartupService();

            _theme = new ThemeService();
            _theme.Initialize(_settings.Current.Theme switch
            {
                "light" => ThemeMode.Light,
                "dark" => ThemeMode.Dark,
                _ => ThemeMode.System
            });
            _theme.Changed += OnThemeChanged;

            _wmi = new WmiMonitorService();
            _displayService = new DisplayService(_wmi);
            _orientationService = new OrientationService();

            _flyoutViewModel = new FlyoutViewModel(_displayService, _wmi, _orientationService, _settings);
            _flyout = new FlyoutWindow(_flyoutViewModel);
            _flyout.SettingsRequested += (_, _) => ShowSettings();
            _flyout.IdentifyRequested += (_, _) => IdentifyDisplays();

            _overlay = new OverlayWindow();

            _tray = new TrayIconService(_theme.TaskbarIsDark);
            _tray.LeftClick += (_, _) => ToggleFlyout();
            _tray.ContextMenuRequested += (_, point) => QueueTrayMenu(point);

            _scrollWatcher = new TrayScrollWatcher(() => _tray?.GetIconRect(), Dispatcher);
            _scrollWatcher.WheelOverTray += OnWheelOverTray;
            _scrollWatcher.SetEnabled(_settings.Current.ScrollOnTray);

            _displayWatcher = new DisplayChangeWatcher();
            _displayWatcher.Changed += OnDisplaysChanged;

            _singleInstance.ShowRequested += (_, _) => ShowFlyout();

            _prefsRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _prefsRefreshTimer.Tick += OnPrefsRefreshTick;

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            _ = WarmupAsync();
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show(
                $"Screen Utility could not start.\n\n{ex.Message}",
                "Screen Utility",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            BeginShutdown();
        }
    }

    private async Task WarmupAsync()
    {
        try
        {
            await _flyoutViewModel!.RefreshAsync();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private void ToggleFlyout()
    {
        if (_shuttingDown || _tray is null || _flyout is null || _overlay is null)
        {
            return;
        }

        // If tray context menu is open, dismiss it
        if (_trayMenu is not null && _trayMenu.IsOpen)
        {
            _trayMenu.IsOpen = false;
            _trayMenu = null;
            return;
        }

        if (_flyout.IsVisible)
        {
            _flyout.HideFlyout();
            return;
        }

        if (_flyout.JustDeactivated)
        {
            return;
        }

        ShowFlyout();
    }

    private async void ShowFlyout()
    {
        if (_shuttingDown || _tray is null || _flyout is null || _overlay is null)
        {
            return;
        }

        try
        {
            _overlay.HideOverlay();
            var rect = _tray.GetIconRect();
            if (rect is null)
            {
                NativeMethods.GetCursorPos(out var cursor);
                rect = PixelRect.FromXywh(cursor.X, cursor.Y, 1, 1);
            }

            await _flyout.OpenAsync(rect.Value);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }


    private void QueueTrayMenu(System.Drawing.Point screenPoint)
    {
        if (_shuttingDown)
        {
            return;
        }

        if (_trayMenu is not null && _trayMenu.IsOpen)
        {
            _trayMenu.IsOpen = false;
            _trayMenu = null;
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ShowTrayMenu(screenPoint)));
    }

    private System.Drawing.Point GetTrayMenuPoint()
    {
        if (_tray?.GetIconRect() is { Width: > 0, Height: > 0 } rect)
        {
            return new System.Drawing.Point(rect.CenterX, rect.CenterY);
        }

        NativeMethods.GetCursorPos(out var cursor);
        return new System.Drawing.Point(cursor.X, cursor.Y);
    }

    private void ShowTrayMenu(System.Drawing.Point screenPoint)
    {
        if (_shuttingDown || _settings is null || _startup is null || _flyoutViewModel is null || _tray is null)
        {
            return;
        }

        _flyout?.HideFlyout();
        if (_trayMenu is not null)
        {
            _trayMenu.IsOpen = false;
            _trayMenu = null;
        }

        // Bring the tray host window to the foreground so Windows handles outside clicks properly
        NativeMethods.SetForegroundWindow(_tray.Handle);

        var point = ToDips(screenPoint);
        var menu = new ContextMenu
        {
            Placement = PlacementMode.AbsolutePoint,
            HorizontalOffset = point.X,
            VerticalOffset = point.Y,
            StaysOpen = false
        };

        var iconFont = (FontFamily)Current.Resources["IconFont"];
        var iconBrush = (Brush)Current.Resources["SecondaryTextBrush"];

        MenuItem CreateItem(string header, string? glyph = null)
        {
            var item = new MenuItem { Header = header };
            if (glyph is not null)
            {
                item.Icon = new TextBlock
                {
                    Text = glyph,
                    FontFamily = iconFont,
                    FontSize = 13,
                    Foreground = iconBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            return item;
        }

        var open = CreateItem("Open Screen Utility", "\uE7F4");
        open.Click += (_, _) => ShowFlyout();

        var identify = CreateItem("Identify displays", "\uE8B3");
        identify.Click += (_, _) => IdentifyDisplays();

        var refresh = CreateItem("Refresh displays", "\uE72C");
        refresh.Click += async (_, _) => await RefreshDisplaysAsync();

        var link = new MenuItem
        {
            Header = "Link brightness",
            IsCheckable = true,
            IsChecked = _settings.Current.LinkedBrightness
        };
        link.Click += (_, _) =>
        {
            var value = link.IsChecked;
            _settings.Update(s => s.LinkedBrightness = value);
            _flyoutViewModel.LinkedBrightness = value;
        };

        var startup = new MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
            IsChecked = _startup.IsEnabled
        };
        startup.Click += (_, _) =>
        {
            var value = startup.IsChecked;
            if (!_startup.SetEnabled(value))
            {
                startup.IsChecked = !value;
                return;
            }

            _settings.Update(s => s.StartWithWindows = value);
        };

        var settingsItem = CreateItem("Settings...", "\uE713");
        settingsItem.Click += (_, _) => ShowSettings();

        var quit = CreateItem("Quit Screen Utility", "\uE711");
        quit.Click += (_, _) => Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(BeginShutdown));

        menu.Items.Add(open);
        menu.Items.Add(identify);
        menu.Items.Add(refresh);
        menu.Items.Add(new Separator());
        menu.Items.Add(link);
        menu.Items.Add(startup);
        menu.Items.Add(new Separator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(quit);

        _trayMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenu, menu))
            {
                _trayMenu = null;
            }
            NativeMethods.PostMessage(_tray.Handle, NativeMethods.WM_NULL, 0, 0);
        };
        menu.IsOpen = true;
    }

    private static Point ToDips(System.Drawing.Point pixelPoint)
    {
        var scale = 1.0;
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT { X = pixelPoint.X, Y = pixelPoint.Y },
            NativeMethods.MONITOR_DEFAULTTONEAREST);

        if (monitor != 0 &&
            NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 &&
            dpiX > 0)
        {
            scale = dpiX / 96.0;
        }

        return new Point(pixelPoint.X / scale, pixelPoint.Y / scale);
    }

    private void OnWheelOverTray(object? sender, int notches)
    {
        if (_shuttingDown || _settings?.Current.ScrollOnTray != true || _flyoutViewModel is null)
        {
            return;
        }

        _ = AdjustBrightnessFromWheelAsync(notches);
    }

    private async Task AdjustBrightnessFromWheelAsync(int notches)
    {
        try
        {
            if (_flyoutViewModel!.Monitors.Count == 0)
            {
                await _flyoutViewModel.RefreshAsync();
            }

            var step = _flyoutViewModel.ScrollStep;
            var direction = (_flyoutViewModel.InvertScroll ? -1 : 1) * Math.Sign(notches) * step;
            var adjustedMonitors = _flyoutViewModel.AdjustTrayBrightness(direction);

            var rows = adjustedMonitors
                .Select(m => new OverlayRow(m.DisplayName, $"{m.BrightnessPercent}%"))
                .ToList();

            if (rows.Count == 0 || _tray is null || _overlay is null)
            {
                return;
            }

            var rect = _tray.GetIconRect();
            if (rect is not null)
            {
                _overlay.ShowBrightness(rect.Value, rows);
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private async void IdentifyDisplays()
    {
        if (_shuttingDown || _displayService is null || _settings is null)
        {
            return;
        }

        _flyout?.HideFlyout();

        try
        {
            foreach (var existing in _identifyWindows.ToList())
            {
                existing.Close();
            }

            _identifyWindows.Clear();

            var descriptors = await _displayService.EnumerateAsync();
            var windows = new List<IdentifyWindow>();
            foreach (var descriptor in descriptors)
            {
                var window = new IdentifyWindow(
                    descriptor.Number,
                    _settings.Current.GetName(descriptor.Key, descriptor.FriendlyName),
                    descriptor.Bounds);
                windows.Add(window);
                _identifyWindows.Add(window);
                window.Show();
            }

            await Task.Delay(2600);

            foreach (var window in windows)
            {
                _identifyWindows.Remove(window);
                window.Close();
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private void ShowSettings()
    {
        if (_shuttingDown || _settings is null || _startup is null || _theme is null || _displayService is null)
        {
            return;
        }

        _flyout?.HideFlyout();

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        try
        {
            var viewModel = new SettingsViewModel(_settings, _startup, _theme, _displayService);
            var window = new SettingsWindow(viewModel, _theme);

            viewModel.ScrollSettingsChanged += OnSettingsScrollChanged;
            viewModel.DisplayPreferencesChanged += OnSettingsDisplayPreferencesChanged;

            window.Closed += (_, _) =>
            {
                viewModel.ScrollSettingsChanged -= OnSettingsScrollChanged;
                viewModel.DisplayPreferencesChanged -= OnSettingsDisplayPreferencesChanged;
                _settingsWindow = null;
                if (!_shuttingDown)
                {
                    _ = _flyoutViewModel?.RefreshAsync();
                }
            };

            _settingsWindow = window;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show(
                $"Unable to open Settings.\n\n{ex.Message}",
                "Screen Utility",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnSettingsScrollChanged(object? sender, EventArgs e)
    {
        _scrollWatcher?.SetEnabled(_settings?.Current.ScrollOnTray == true);
        _flyoutViewModel?.ApplyDisplayPreferences();
    }

    private void OnSettingsDisplayPreferencesChanged(object? sender, EventArgs e)
    {
        _flyoutViewModel?.ApplyDisplayPreferences();

        if (_prefsRefreshTimer is null)
        {
            return;
        }

        _prefsRefreshTimer.Stop();
        _prefsRefreshTimer.Start();
    }

    private async void OnPrefsRefreshTick(object? sender, EventArgs e)
    {
        _prefsRefreshTimer?.Stop();
        await RefreshDisplaysAsync();
    }

    private async Task RefreshDisplaysAsync()
    {
        try
        {
            if (_flyoutViewModel is not null)
            {
                await _flyoutViewModel.RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private async void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (_shuttingDown)
        {
            return;
        }

        try
        {
            if (_wmi is not null)
            {
                await _wmi.EnsureLoadedAsync(force: true);
            }

            _flyoutViewModel?.ApplyDisplayPreferences();

            if (_flyout?.IsVisible != true)
            {
                await RefreshDisplaysAsync();
            }

            if (_settingsWindow is not null)
            {
                await _settingsWindow.ReloadDisplaysAsync();
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private void BeginShutdown()
    {
        _shuttingDown = true;
        _flyout?.ClosePermanently();
        Shutdown();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_theme is not null && _tray is not null)
        {
            _tray.SetTaskbarTheme(_theme.TaskbarIsDark);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
    }

    private static void Log(Exception exception)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScreenUtility");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "error.log"),
                $"[{DateTimeOffset.Now:O}] {exception}\n\n");
        }
        catch
        {
            // Logging must never throw during error handling.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        _prefsRefreshTimer?.Stop();

        if (_trayMenu is not null)
        {
            _trayMenu.IsOpen = false;
            _trayMenu = null;
        }

        foreach (var window in _identifyWindows.ToList())
        {
            window.Close();
        }

        _identifyWindows.Clear();

        _scrollWatcher?.Dispose();
        _displayWatcher?.Dispose();
        _flyoutViewModel?.Dispose();

        _settingsWindow?.Close();
        _settingsWindow = null;
        _flyout?.ClosePermanently();
        _overlay?.HideOverlay();
        _overlay?.Close();

        _tray?.Dispose();
        _singleInstance?.Dispose();
        _theme?.Dispose();

        base.OnExit(e);
    }
}
