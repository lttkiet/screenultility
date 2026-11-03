namespace ScreenUtility.Utils;

public static class ValueMap
{
    public static int ClampPercent(int value) => Math.Clamp(value, 0, 100);

    public static int RawToPercent(int raw, int min, int max)
    {
        if (max <= min)
        {
            return 0;
        }

        var percent = (int)Math.Round((raw - min) * 100.0 / (max - min), MidpointRounding.AwayFromZero);
        return ClampPercent(percent);
    }

    public static int PercentToRaw(int percent, int min, int max)
    {
        if (max <= min)
        {
            return min;
        }

        var clamped = ClampPercent(percent);
        return min + (int)Math.Round(clamped * (max - min) / 100.0, MidpointRounding.AwayFromZero);
    }
}
