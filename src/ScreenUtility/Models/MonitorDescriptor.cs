namespace ScreenUtility.Models;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public static PixelRect FromLtrb(int left, int top, int right, int bottom) => new(left, top, right, bottom);
    public static PixelRect FromXywh(int x, int y, int width, int height) => new(x, y, x + width, y + height);

    public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
}

public sealed class MonitorDescriptor
{
    public required string Key { get; init; }
    public required string DeviceName { get; init; }
    public required string FriendlyName { get; init; }
    public string? MonitorId { get; init; }
    public string? PnpCode { get; init; }
    public PixelRect Bounds { get; init; }
    public PixelRect WorkArea { get; init; }
    public bool IsPrimary { get; init; }
    public int Number { get; init; }
    public nint Handle { get; init; }

    public string ResolutionText => $"{Bounds.Width} x {Bounds.Height}";
    public string PositionText => $"{Bounds.Left}, {Bounds.Top}";
    public string DeviceLabel => DeviceName.Replace(@"\\.\", "");
}
