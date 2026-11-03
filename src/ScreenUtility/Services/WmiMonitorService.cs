using System.Management;

namespace ScreenUtility.Services;

public sealed record WmiBrightnessPanel(string InstanceName, string PnpCode, int Current, int Max);

/// <summary>
/// Caches the root\wmi monitor metadata used for internal panel brightness and friendly names.
/// </summary>
public sealed class WmiMonitorService
{
    private static readonly ManagementScope ScopeWmi = new(@"\\.\root\wmi");
    private static readonly ManagementScope ScopeCim = new(@"\\.\root\cimv2");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Snapshot _snapshot = new();
    private volatile bool _loaded;

    public bool IsLoaded => _loaded;

    private sealed class Snapshot
    {
        public Dictionary<string, string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, WmiBrightnessPanel> Brightness { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<WmiBrightnessPanel> BrightnessPanels { get; } = new();
    }

    public async Task EnsureLoadedAsync(bool force = false)
    {
        if (_loaded && !force)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_loaded && !force)
            {
                return;
            }

            await Task.Run(LoadCore).ConfigureAwait(false);
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void LoadCore()
    {
        var snapshot = new Snapshot();

        // 1. Read monitor friendly names from WmiMonitorID (EDID-extracted strings)
        try
        {
            using var searcher = new ManagementObjectSearcher(ScopeWmi, new ObjectQuery("SELECT InstanceName, UserFriendlyName, ManufacturerName, ProductCodeID FROM WmiMonitorID"));
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                try
                {
                    var instance = item["InstanceName"] as string;
                    if (string.IsNullOrWhiteSpace(instance))
                    {
                        continue;
                    }

                    var pnp = ExtractPnpCode(instance);
                    var norm = NormalizeDeviceId(instance);

                    var name = DecodeString(item["UserFriendlyName"] as ushort[]);
                    var clean = CleanMonitorName(name);

                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        if (!string.IsNullOrEmpty(norm))
                        {
                            snapshot.Names[norm] = clean;
                        }
                        if (pnp is not null)
                        {
                            snapshot.Names[pnp] = clean;
                        }
                    }
                }
                catch
                {
                    // A single malformed WMI instance should not break enumeration.
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch
        {
            // WMI may be unavailable or access denied.
        }

        // 2. Supplement names from Win32_PnPEntity (provides "Integrated Monitor", hardware models)
        try
        {
            using var pnpSearcher = new ManagementObjectSearcher(ScopeCim, new ObjectQuery("SELECT DeviceID, Name FROM Win32_PnPEntity WHERE PNPClass = 'Monitor'"));
            using var pnpResults = pnpSearcher.Get();
            foreach (ManagementObject item in pnpResults)
            {
                try
                {
                    var devId = item["DeviceID"] as string;
                    var rawName = item["Name"] as string;
                    var clean = CleanMonitorName(rawName);

                    if (!string.IsNullOrWhiteSpace(devId) && !string.IsNullOrWhiteSpace(clean))
                    {
                        var pnp = ExtractPnpCode(devId);
                        var norm = NormalizeDeviceId(devId);

                        if (!string.IsNullOrEmpty(norm) && (!snapshot.Names.ContainsKey(norm) || IsGeneric(snapshot.Names[norm])))
                        {
                            snapshot.Names[norm] = clean;
                        }

                        if (pnp is not null && (!snapshot.Names.ContainsKey(pnp) || IsGeneric(snapshot.Names[pnp])))
                        {
                            snapshot.Names[pnp] = clean;
                        }
                    }
                }
                catch
                {
                    // Ignore PnP entity errors.
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch
        {
            // Optional fallback.
        }

        // 3. Read WMI brightness capabilities for internal/laptop panels
        try
        {
            using var searcher = new ManagementObjectSearcher(ScopeWmi, new ObjectQuery("SELECT InstanceName, CurrentBrightness, Levels FROM WmiMonitorBrightness"));
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                try
                {
                    var instance = item["InstanceName"] as string;
                    if (string.IsNullOrWhiteSpace(instance))
                    {
                        continue;
                    }

                    var pnp = ExtractPnpCode(instance);
                    var norm = NormalizeDeviceId(instance);

                    var levels = item["Levels"] as byte[];
                    var max = levels is { Length: > 0 } ? levels.Max() : 100;
                    var current = Convert.ToInt32(item["CurrentBrightness"] ?? 0);
                    var panel = new WmiBrightnessPanel(instance, pnp ?? "INTERNAL", Math.Clamp(current, 0, max), max);

                    if (!string.IsNullOrEmpty(norm))
                    {
                        snapshot.Brightness[norm] = panel;
                    }

                    if (pnp is not null)
                    {
                        snapshot.Brightness[pnp] = panel;
                    }

                    snapshot.BrightnessPanels.Add(panel);

                    // Built-in panels that lack an EDID UserFriendlyName should be labeled cleanly
                    if (!string.IsNullOrEmpty(norm) && (!snapshot.Names.ContainsKey(norm) || IsGeneric(snapshot.Names[norm])))
                    {
                        snapshot.Names[norm] = "Built-in display";
                    }

                    if (pnp is not null && (!snapshot.Names.ContainsKey(pnp) || IsGeneric(snapshot.Names[pnp])))
                    {
                        snapshot.Names[pnp] = "Built-in display";
                    }
                }
                catch
                {
                    // Ignore individual panels.
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch
        {
            // No WMI brightness support (typical for desktops/external-only setups).
        }

        _snapshot = snapshot;
    }

    public string? GetFriendlyName(string? deviceId, string? pnpCode = null)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var norm = NormalizeDeviceId(deviceId);
            if (!string.IsNullOrEmpty(norm) && _snapshot.Names.TryGetValue(norm, out var normName) && !IsGeneric(normName))
            {
                return normName;
            }
        }

        if (!string.IsNullOrWhiteSpace(pnpCode) && _snapshot.Names.TryGetValue(pnpCode, out var pnpName) && !IsGeneric(pnpName))
        {
            return pnpName;
        }

        return null;
    }

    public bool IsInternalPanel(string? deviceId, string? pnpCode = null)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var norm = NormalizeDeviceId(deviceId);
            if (!string.IsNullOrEmpty(norm) && _snapshot.Brightness.ContainsKey(norm))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(pnpCode) && _snapshot.Brightness.ContainsKey(pnpCode))
        {
            return true;
        }

        return false;
    }

    public WmiBrightnessPanel? GetBrightnessPanel(string? deviceId, string? pnpCode = null)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var norm = NormalizeDeviceId(deviceId);
            if (!string.IsNullOrEmpty(norm) && _snapshot.Brightness.TryGetValue(norm, out var normPanel))
            {
                return normPanel;
            }
        }

        if (!string.IsNullOrWhiteSpace(pnpCode) && _snapshot.Brightness.TryGetValue(pnpCode, out var pnpPanel))
        {
            return pnpPanel;
        }

        // Only fallback to single panel if the monitor has no explicit conflicting PnP code
        if (_snapshot.BrightnessPanels.Count == 1 && string.IsNullOrWhiteSpace(pnpCode))
        {
            return _snapshot.BrightnessPanels[0];
        }

        return null;
    }

    public async Task<bool> SetBrightnessAsync(string instanceName, int percent)
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        return await Task.Run(() => SetBrightnessCore(instanceName, percent)).ConfigureAwait(false);
    }

    private static bool SetBrightnessCore(string instanceName, int percent)
    {
        try
        {
            var clamped = (byte)Math.Clamp(percent, 0, 100);
            var escaped = instanceName.Replace("\\", "\\\\").Replace("'", "\\'");
            using var searcher = new ManagementObjectSearcher(
                ScopeWmi,
                new ObjectQuery($"SELECT * FROM WmiMonitorBrightnessMethods WHERE InstanceName = '{escaped}'"));
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                try
                {
                    item.InvokeMethod("WmiSetBrightness", new object[] { 0u, clamped });
                    return true;
                }
                finally
                {
                    item.Dispose();
                }
            }

            // Fallback: search all brightness methods and match by PNP code or first available panel
            var pnp = ExtractPnpCode(instanceName);
            using var fallbackSearcher = new ManagementObjectSearcher(ScopeWmi, new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
            using var fallbackResults = fallbackSearcher.Get();
            foreach (ManagementObject item in fallbackResults)
            {
                try
                {
                    var inst = item["InstanceName"] as string;
                    if (pnp is null || ExtractPnpCode(inst)?.Equals(pnp, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        item.InvokeMethod("WmiSetBrightness", new object[] { 0u, clamped });
                        return true;
                    }
                }
                finally
                {
                    item.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WMI SetBrightness failed: {ex.Message}");
        }

        return false;
    }

    public static string? ExtractPnpCode(string? instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return null;
        }

        var cleaned = instanceName.Trim();
        if (cleaned.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            cleaned = cleaned[4..];
        }

        var parts = cleaned.Split(new[] { '\\', '#' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i + 1].ToUpperInvariant();
            }
        }

        return null;
    }

    public static string NormalizeDeviceId(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
        {
            return string.Empty;
        }

        var cleaned = rawId.Trim();
        if (cleaned.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            cleaned = cleaned[4..];
        }

        var parts = cleaned.Split(new[] { '\\', '#' }, StringSplitOptions.RemoveEmptyEntries);
        var start = 0;
        while (start < parts.Length &&
               !parts[start].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase) &&
               !parts[start].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
        {
            start++;
        }

        if (start + 2 < parts.Length)
        {
            var instancePart = parts[start + 2];
            if (instancePart.EndsWith("_0", StringComparison.OrdinalIgnoreCase))
            {
                instancePart = instancePart[..^2];
            }

            return $"{parts[start].ToUpperInvariant()}\\{parts[start + 1].ToUpperInvariant()}\\{instancePart.ToUpperInvariant()}";
        }

        if (start + 1 < parts.Length)
        {
            return $"{parts[start].ToUpperInvariant()}\\{parts[start + 1].ToUpperInvariant()}";
        }

        return cleaned.ToUpperInvariant();
    }

    public static string? CleanMonitorName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return null;
        }

        var trimmed = rawName.Trim();
        if (IsGeneric(trimmed))
        {
            return null;
        }

        if (trimmed.Equals("Integrated Monitor", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Internal Display", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Built-in Display", StringComparison.OrdinalIgnoreCase))
        {
            return "Built-in display";
        }

        var openParen = trimmed.IndexOf('(');
        var closeParen = trimmed.LastIndexOf(')');
        if (openParen >= 0 && closeParen > openParen)
        {
            var inside = trimmed.Substring(openParen + 1, closeParen - openParen - 1).Trim();
            if (!string.IsNullOrWhiteSpace(inside) && !IsGeneric(inside))
            {
                return inside;
            }
        }

        return trimmed;
    }

    public static bool IsGeneric(string? name) =>
        string.IsNullOrWhiteSpace(name) ||
        name.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Generic Non-PnP Monitor", StringComparison.OrdinalIgnoreCase);

    private static string? DecodeString(ushort[]? value)
    {
        if (value is null || value.Length == 0)
        {
            return null;
        }

        var chars = value.TakeWhile(c => c != 0).Select(c => (char)c).ToArray();
        var text = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
