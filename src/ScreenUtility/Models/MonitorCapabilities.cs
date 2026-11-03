namespace ScreenUtility.Models;

public enum BrightnessSource
{
    None,
    DdcCi,
    Wmi
}

public sealed class MonitorCapabilities
{
    public BrightnessSource BrightnessSource { get; init; } = BrightnessSource.None;
    public int BrightnessMin { get; init; } = 0;
    public int BrightnessMax { get; init; } = 100;
    public int Brightness { get; init; } = 0;

    public bool HasContrast { get; init; }
    public int ContrastMin { get; init; } = 0;
    public int ContrastMax { get; init; } = 100;
    public int Contrast { get; init; } = 0;

    public DisplayOrientation Orientation { get; init; } = DisplayOrientation.Landscape;

    public bool HasBrightness => BrightnessSource != BrightnessSource.None;
}
