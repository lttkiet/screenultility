using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using ScreenUtility.Interop;

namespace ScreenUtility.Theming;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public sealed class ThemeService : IDisposable
{
    private static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x78, 0xD4);
    private ThemeMode _mode = ThemeMode.System;
    private bool _disposed;

    public bool IsDark { get; private set; }
    public bool TaskbarIsDark { get; private set; }
    public Color AccentColor { get; private set; } = FallbackAccent;
    public ThemeMode Mode => _mode;

    public event EventHandler? Changed;

    public void Initialize(ThemeMode mode)
    {
        _mode = mode;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply();
    }

    public void SetMode(ThemeMode mode)
    {
        _mode = mode;
        Apply();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.BeginInvoke(new Action(Apply));
        }
    }

    public void Apply()
    {
        if (_disposed)
        {
            return;
        }

        var systemDark = IsSystemDark();
        IsDark = _mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => systemDark
        };

        TaskbarIsDark = !ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 1);
        AccentColor = ReadAccentColor();

        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        SetBrush(resources, "PanelBackgroundBrush", IsDark ? "#242424" : "#F2F2F2");
        SetBrush(resources, "PanelBorderBrush", IsDark ? "#3A3A3A" : "#D6D6D6");
        SetBrush(resources, "PrimaryTextBrush", IsDark ? "#FFFFFF" : "#1B1B1B");
        SetBrush(resources, "SecondaryTextBrush", IsDark ? "#AFAFAF" : "#5E5E5E");
        SetBrush(resources, "TertiaryTextBrush", IsDark ? "#8A8A8A" : "#7A7A7A");
        SetBrush(resources, "ControlTrackBrush", IsDark ? "#9B9B9B" : "#868686");
        SetBrush(resources, "ThumbBorderBrush", "#FFFFFF");
        SetBrush(resources, "InputBackgroundBrush", IsDark ? "#2E2E2E" : "#FFFFFF");
        SetBrush(resources, "HoverBrush", IsDark ? "#18FFFFFF" : "#0F000000");
        SetBrush(resources, "PressedBrush", IsDark ? "#25FFFFFF" : "#1A000000");
        SetBrush(resources, "MenuBackgroundBrush", IsDark ? "#242424" : "#FBFBFB");
        SetBrush(resources, "MenuBorderBrush", IsDark ? "#383838" : "#D8D8D8");
        SetBrush(resources, "CardBackgroundBrush", IsDark ? "#2B2B2B" : "#FFFFFF");
        SetBrush(resources, "DangerBrush", IsDark ? "#FF99A4" : "#C42B1C");
        SetBrush(resources, "OverlayBackgroundBrush", "#E61C1C1C");
        SetBrush(resources, "OverlayTextBrush", "#FFFFFF");
        SetBrush(resources, "SeperatorBrush", IsDark ? "#3A3A3A" : "#E3E3E3");

        var accent = AccentColor;
        SetBrush(resources, "AccentBrush", accent);
        SetBrush(resources, "AccentHoverBrush", Shift(accent, IsDark ? 0.12 : -0.08));
        SetBrush(resources, "AccentPressedBrush", Shift(accent, IsDark ? -0.10 : -0.16));
        SetBrush(resources, "AccentSoftBrush", WithAlpha(accent, IsDark ? (byte)0x32 : (byte)0x1F));
        SetBrush(resources, "AccentTextBrush", Luminance(accent) > 0.55 ? Brushes.Black.Color : Brushes.White.Color);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex) =>
        resources[key] = CreateBrush((Color)ColorConverter.ConvertFromString(hex));

    private static void SetBrush(ResourceDictionary resources, string key, Color color) =>
        resources[key] = CreateBrush(color);

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Shift(Color color, double amount)
    {
        static byte Apply(byte value, double delta) =>
            (byte)Math.Clamp(delta >= 0 ? value + (255 - value) * delta : value + value * delta, 0, 255);

        return Color.FromArgb(color.A, Apply(color.R, amount), Apply(color.G, amount), Apply(color.B, amount));
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

    private static bool IsSystemDark() =>
        !ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);

    private static bool ReadDword(string path, string name, int fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path, false);
            if (key?.GetValue(name) is int value)
            {
                return value != 0;
            }
        }
        catch
        {
            // Fall through to the provided default.
        }

        return fallback != 0;
    }

    private static Color ReadAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", false);
            if (key?.GetValue("ColorizationColor") is int value)
            {
                var color = Color.FromArgb(
                    (byte)((value >> 24) & 0xFF),
                    (byte)((value >> 16) & 0xFF),
                    (byte)((value >> 8) & 0xFF),
                    (byte)(value & 0xFF));
                if (color.R + color.G + color.B > 30)
                {
                    return Color.FromRgb(color.R, color.G, color.B);
                }
            }
        }
        catch
        {
            // Fall back below.
        }

        return FallbackAccent;
    }

    public static void ApplyWindowTheme(Window window, bool dark, bool rounded = true)
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == 0)
            {
                return;
            }

            var darkValue = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));

            if (rounded)
            {
                var preference = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
        }
        catch
        {
            // DWM theming is cosmetic and unavailable on older Windows versions.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
