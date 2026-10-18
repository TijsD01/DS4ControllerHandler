using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DS4Handler.App.Engine;
using DS4Handler.App.Infrastructure;

namespace DS4Handler.App.Services;

/// <summary>Per-controller choices of the user (not the calibration itself; that lives in the profile).</summary>
public sealed class ControllerPreferences
{
    public bool LearningEnabled { get; set; } = true;
    public bool HideFromGames { get; set; } = true;

    /// <summary>Which virtual controller games get for this pad.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutputMode Output { get; set; } = OutputMode.Xbox360;
}

/// <summary>App settings, saved as JSON in %APPDATA%\DS4ControllerHandler\settings.json.</summary>
public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }

    /// <summary>Use HidHide to hide the real controllers from games (only the virtual Xbox pad stays visible).</summary>
    public bool HidHideEnabled { get; set; } = true;

    /// <summary>Make hidden controllers visible again when the app closes.</summary>
    public bool UnhideOnExit { get; set; } = true;

    /// <summary>Shown once: the window closes to the tray instead of quitting.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>Look on GitHub for a newer version when the app starts (and once a day after that).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Version that ran last time, to show "what's new" once after an update.</summary>
    public string? LastRunVersion { get; set; }

    public Dictionary<string, ControllerPreferences> Controllers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>HidHide device instance ids this app put on the hide list, so they can be undone.</summary>
    public List<string> HiddenInstanceIds { get; set; } = new();

    public ControllerPreferences For(string controllerId)
    {
        if (!Controllers.TryGetValue(controllerId, out var prefs))
        {
            prefs = new ControllerPreferences();
            Controllers[controllerId] = prefs;
        }
        return prefs;
    }
}

/// <summary>Loads and saves <see cref="AppSettings"/>. Thread-safe; saving is atomic (temp file + move).</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();

    public SettingsStore(string? path = null)
    {
        FilePath = path ?? Path.Combine(Log.DataDirectory, "settings.json");
        Settings = Load();
    }

    public string FilePath { get; }

    /// <summary>True when settings.json was already there at startup (so this is not a fresh install).</summary>
    public bool ExistedAtStartup { get; private set; }

    /// <summary>The live settings. Change them under <see cref="Update"/> so saving sees a consistent copy.</summary>
    public AppSettings Settings { get; }

    /// <summary>Changes the settings under the lock and saves them.</summary>
    public void Update(Action<AppSettings> change)
    {
        lock (_gate)
        {
            change(Settings);
            SaveLocked();
        }
    }

    /// <summary>Reads a value under the lock.</summary>
    public T Read<T>(Func<AppSettings, T> read)
    {
        lock (_gate) return read(Settings);
    }

    public void Save()
    {
        lock (_gate) SaveLocked();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                ExistedAtStartup = true;
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                if (loaded != null)
                {
                    // Keep lookups case-insensitive and drop nulls from hand-edited files.
                    var prefs = new Dictionary<string, ControllerPreferences>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (id, p) in loaded.Controllers ?? new()) if (p != null) prefs[id] = p;
                    loaded.Controllers = prefs;
                    loaded.HiddenInstanceIds = (loaded.HiddenInstanceIds ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Instellingen lezen mislukt; standaardinstellingen worden gebruikt.", ex);
        }
        return new AppSettings();
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Settings, Json), Encoding.UTF8);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("Instellingen opslaan mislukt", ex);
        }
    }
}
