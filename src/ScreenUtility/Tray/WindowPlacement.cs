using ScreenUtility.Models;

namespace ScreenUtility.Tray;

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right
}

/// <summary>
/// Pure placement calculations for the tray flyout and brightness overlay.
/// All values are physical pixels.
/// </summary>
public static class WindowPlacement
{
    public const int DefaultGap = 10;
    public const int EdgeMargin = 8;

    public static TaskbarEdge GetTaskbarEdge(PixelRect monitor, PixelRect work, PixelRect tray)
    {
        if (work.Bottom < monitor.Bottom) return TaskbarEdge.Bottom;
        if (work.Top > monitor.Top) return TaskbarEdge.Top;
        if (work.Left > monitor.Left) return TaskbarEdge.Left;
        if (work.Right < monitor.Right) return TaskbarEdge.Right;

        // Auto-hide taskbar or unavailable work area: infer from the tray icon.
        var dx = tray.CenterX - monitor.CenterX;
        var dy = tray.CenterY - monitor.CenterY;
        return Math.Abs(dy) >= Math.Abs(dx)
            ? (dy > 0 ? TaskbarEdge.Bottom : TaskbarEdge.Top)
            : (dx > 0 ? TaskbarEdge.Right : TaskbarEdge.Left);
    }

    public static PixelRect PlaceFlyout(PixelRect tray, PixelRect monitor, PixelRect work, int width, int height, int gap = DefaultGap)
    {
        width = Math.Min(width, Math.Max(1, work.Width - EdgeMargin * 2));
        height = Math.Min(height, Math.Max(1, work.Height - EdgeMargin * 2));
        var edge = GetTaskbarEdge(monitor, work, tray);

        var x = edge switch
        {
            TaskbarEdge.Bottom or TaskbarEdge.Top => tray.Right - width,
            TaskbarEdge.Left => work.Left + gap,
            _ => work.Right - width - gap
        };

        var y = edge switch
        {
            TaskbarEdge.Bottom => work.Bottom - height - gap,
            TaskbarEdge.Top => work.Top + gap,
            _ => work.Bottom - height - gap
        };

        return ClampInto(new PixelRect(x, y, x + width, y + height), work);
    }

    public static PixelRect PlaceOverlay(PixelRect tray, PixelRect monitor, PixelRect work, int width, int height, int gap = DefaultGap)
    {
        width = Math.Min(width, Math.Max(1, work.Width - EdgeMargin * 2));
        height = Math.Min(height, Math.Max(1, work.Height - EdgeMargin * 2));
        var edge = GetTaskbarEdge(monitor, work, tray);

        var x = edge switch
        {
            TaskbarEdge.Bottom or TaskbarEdge.Top => tray.CenterX - width / 2,
            TaskbarEdge.Left => work.Left + gap,
            _ => work.Right - width - gap
        };

        var y = edge switch
        {
            TaskbarEdge.Bottom => work.Bottom - height - gap,
            TaskbarEdge.Top => work.Top + gap,
            TaskbarEdge.Left or TaskbarEdge.Right => tray.CenterY - height / 2,
            _ => work.Bottom - height - gap
        };

        return ClampInto(new PixelRect(x, y, x + width, y + height), work);
    }

    public static PixelRect ClampInto(PixelRect rect, PixelRect bounds)
    {
        var availableWidth = Math.Max(1, bounds.Width - EdgeMargin * 2);
        var availableHeight = Math.Max(1, bounds.Height - EdgeMargin * 2);
        var width = Math.Min(rect.Width, availableWidth);
        var height = Math.Min(rect.Height, availableHeight);
        var x = Math.Clamp(rect.Left, bounds.Left + EdgeMargin, Math.Max(bounds.Left + EdgeMargin, bounds.Right - width - EdgeMargin));
        var y = Math.Clamp(rect.Top, bounds.Top + EdgeMargin, Math.Max(bounds.Top + EdgeMargin, bounds.Bottom - height - EdgeMargin));
        return new PixelRect(x, y, x + width, y + height);
    }
}
