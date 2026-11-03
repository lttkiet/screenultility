using System.Runtime.InteropServices;
using ScreenUtility.Interop;
using ScreenUtility.Models;

namespace ScreenUtility.Services;

public enum OrientationApplyStatus
{
    Success,
    Unchanged,
    BadMode,
    BadParameter,
    Failed,
    RestartRequired
}

public readonly record struct OrientationApplyResult(
    OrientationApplyStatus Status,
    DisplayOrientation Orientation,
    string? Message)
{
    public bool Succeeded => Status is OrientationApplyStatus.Success or OrientationApplyStatus.Unchanged;
}

public static class OrientationMath
{
    /// <summary>
    /// Returns the pixel dimensions a mode should use after rotating. DEVMODE expects a
    /// landscape-sized mode description; portrait modes swap width and height.
    /// </summary>
    public static (int Width, int Height) ComputeDimensions(DisplayOrientation current, DisplayOrientation target, int width, int height)
    {
        if (current.IsPortrait() == target.IsPortrait())
        {
            return (width, height);
        }

        return (height, width);
    }
}

public sealed class OrientationService
{
    public DisplayOrientation GetCurrent(string deviceName)
    {
        var devMode = CreateDevMode();
        if (!NativeMethods.EnumDisplaySettingsEx(deviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref devMode, 0))
        {
            return DisplayOrientation.Landscape;
        }

        return Normalize(devMode.dmDisplayOrientation);
    }

    public OrientationApplyResult Set(string deviceName, DisplayOrientation target)
    {
        var currentMode = CreateDevMode();
        if (!NativeMethods.EnumDisplaySettingsEx(deviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref currentMode, 0))
        {
            return new OrientationApplyResult(OrientationApplyStatus.Failed, target, "Could not read the current display mode.");
        }

        var current = Normalize(currentMode.dmDisplayOrientation);
        if (current == target)
        {
            return new OrientationApplyResult(OrientationApplyStatus.Unchanged, current, null);
        }

        var (width, height) = OrientationMath.ComputeDimensions(
            current,
            target,
            currentMode.dmPelsWidth,
            currentMode.dmPelsHeight);

        var mode = currentMode;
        mode.dmDisplayOrientation = (int)target;
        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmFields = NativeMethods.DM_DISPLAYORIENTATION | NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT;
        mode.dmDeviceName = string.Empty;
        mode.dmFormName = string.Empty;

        var result = NativeMethods.ChangeDisplaySettingsEx(
            deviceName,
            ref mode,
            nint.Zero,
            NativeMethods.CDS_UPDATEREGISTRY,
            nint.Zero);

        if (result == (int)DisplayChangeResult.BadMode || result == (int)DisplayChangeResult.BadParam)
        {
            // Some drivers reject dimension changes while rotating. Retry orientation-only.
            var orientationOnly = currentMode;
            orientationOnly.dmDisplayOrientation = (int)target;
            orientationOnly.dmFields = NativeMethods.DM_DISPLAYORIENTATION;
            orientationOnly.dmDeviceName = string.Empty;
            orientationOnly.dmFormName = string.Empty;
            result = NativeMethods.ChangeDisplaySettingsEx(
                deviceName,
                ref orientationOnly,
                nint.Zero,
                NativeMethods.CDS_UPDATEREGISTRY,
                nint.Zero);
        }

        return result switch
        {
            0 => new OrientationApplyResult(OrientationApplyStatus.Success, target, null),
            1 => new OrientationApplyResult(OrientationApplyStatus.RestartRequired, target, "The display change will apply after a restart."),
            -1 => new OrientationApplyResult(OrientationApplyStatus.Failed, current, "The display driver rejected the change."),
            -2 => new OrientationApplyResult(OrientationApplyStatus.BadMode, current, "This display does not support that orientation."),
            -3 => new OrientationApplyResult(OrientationApplyStatus.Failed, current, "The display settings were not updated."),
            -4 => new OrientationApplyResult(OrientationApplyStatus.BadParameter, current, "The display mode is invalid for this monitor."),
            -5 => new OrientationApplyResult(OrientationApplyStatus.Failed, current, "The display flags are not supported."),
            -6 => new OrientationApplyResult(OrientationApplyStatus.Failed, current, "The display device is not available."),
            -7 => new OrientationApplyResult(OrientationApplyStatus.Failed, current, "The display driver does not support rotation."),
            _ => new OrientationApplyResult(OrientationApplyStatus.Failed, current, $"Display change failed (code {result}).")
        };
    }

    private static NativeMethods.DEVMODE CreateDevMode() => new()
    {
        dmSize = (short)Marshal.SizeOf<NativeMethods.DEVMODE>(),
        dmDeviceName = string.Empty,
        dmFormName = string.Empty
    };

    private static DisplayOrientation Normalize(int value) => value switch
    {
        1 => DisplayOrientation.Portrait,
        2 => DisplayOrientation.LandscapeFlipped,
        3 => DisplayOrientation.PortraitFlipped,
        _ => DisplayOrientation.Landscape
    };

    private enum DisplayChangeResult
    {
        Successful = 0,
        Restart = 1,
        Failed = -1,
        BadMode = -2,
        NotUpdated = -3,
        BadParam = -4,
        BadFlags = -5,
        BadDevice = -6,
        BadDriver = -7
    }
}
