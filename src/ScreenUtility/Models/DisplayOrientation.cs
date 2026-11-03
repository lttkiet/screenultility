namespace ScreenUtility.Models;

/// <summary>
/// Mirrors the DEVMODE dmDisplayOrientation values.
/// </summary>
public enum DisplayOrientation
{
    Landscape = 0,
    Portrait = 1,
    LandscapeFlipped = 2,
    PortraitFlipped = 3
}

public static class DisplayOrientationExtensions
{
    public static bool IsPortrait(this DisplayOrientation orientation) =>
        orientation is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped;

    public static string ToDisplayName(this DisplayOrientation orientation) => orientation switch
    {
        DisplayOrientation.Portrait => "Portrait",
        DisplayOrientation.LandscapeFlipped => "Landscape (flipped)",
        DisplayOrientation.PortraitFlipped => "Portrait (flipped)",
        _ => "Landscape"
    };

    public static string ToShortName(this DisplayOrientation orientation) => orientation switch
    {
        DisplayOrientation.Portrait => "Portrait",
        DisplayOrientation.LandscapeFlipped => "Landscape flipped",
        DisplayOrientation.PortraitFlipped => "Portrait flipped",
        _ => "Landscape"
    };
}
