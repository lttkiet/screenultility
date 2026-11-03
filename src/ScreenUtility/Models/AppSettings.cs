namespace ScreenUtility.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "system";
    public bool ScrollOnTray { get; set; } = true;
    public int ScrollStep { get; set; } = 2;
    public bool InvertScroll { get; set; }
    public bool LinkedBrightness { get; set; }
    public bool StartWithWindows { get; set; }
    public Dictionary<string, string> MonitorNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> MonitorOrder { get; set; } = new();
    public HashSet<string> HiddenMonitors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AppSettings Clone() => new()
    {
        Theme = Theme,
        ScrollOnTray = ScrollOnTray,
        ScrollStep = ScrollStep,
        InvertScroll = InvertScroll,
        LinkedBrightness = LinkedBrightness,
        StartWithWindows = StartWithWindows,
        MonitorNames = new Dictionary<string, string>(MonitorNames, StringComparer.OrdinalIgnoreCase),
        MonitorOrder = new List<string>(MonitorOrder),
        HiddenMonitors = new HashSet<string>(HiddenMonitors, StringComparer.OrdinalIgnoreCase)
    };

    public string GetName(string key, string fallback) =>
        MonitorNames.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name) ? name : fallback;
}
