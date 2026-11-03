using System.IO;
using System.Text.Json;
using ScreenUtility.Models;

namespace ScreenUtility.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string _path;
    private AppSettings _settings = new();

    public SettingsStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenUtility");
        _path = Path.Combine(folder, "settings.json");
        Load();
    }

    public string FilePath => _path;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null)
            {
                return;
            }

            loaded.MonitorNames = new Dictionary<string, string>(loaded.MonitorNames ?? new(), StringComparer.OrdinalIgnoreCase);
            loaded.MonitorOrder ??= new List<string>();
            loaded.HiddenMonitors = new HashSet<string>(loaded.HiddenMonitors ?? new(), StringComparer.OrdinalIgnoreCase);
            loaded.ScrollStep = Math.Clamp(loaded.ScrollStep, 1, 20);
            loaded.Theme = loaded.Theme is "light" or "dark" ? loaded.Theme : "system";

            lock (_gate)
            {
                _settings = loaded;
            }
        }
        catch
        {
            // Corrupt or unreadable settings fall back to defaults.
        }
    }

    public void Update(Action<AppSettings> mutate)
    {
        lock (_gate)
        {
            mutate(_settings);
            SaveCore(_settings);
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            SaveCore(_settings);
        }
    }

    private void SaveCore(AppSettings settings)
    {
        try
        {
            var folder = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(folder);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, _path, true);
        }
        catch
        {
            // Never let settings persistence crash the tray application.
        }
    }
}
