using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DS4Handler.App.Engine;
using DS4Handler.App.Infrastructure;
using DS4Handler.Core.Persistence;

namespace DS4Handler.App.Services;

/// <summary>Per-controller choices of the user (not the calibration itself; that lives in the profile).</summary>
public sealed class ControllerPreferences
{
    public bool LearningEnabled { get; set; } = true;

    /// <summary>Smooth out stick jitter with the learned jitter filter.</summary>
    public bool JitterFilterEnabled { get; set; } = true;

    public bool HideFromGames { get; set; } = true;

    /// <summary>Which virtual controller games get for this pad.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutputMode Output { get; set; } = OutputMode.Xbox360;

    /// <summary>Friendly name chosen by the user ("Controller van Sem"); null = model name. Never changes the file key.</summary>
    public string? Name { get; set; }

    /// <summary>When this controller was last connected (UTC).</summary>
    public DateTime? LastSeenUtc { get; set; }

    /// <summary>Profile id this controller learns into instead of its own (a shared profile); null = own.</summary>
    public string? LinkedProfileId { get; set; }
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

    /// <summary>Look on GitHub for a newer version when the app starts (and every 12 hours after that).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Version that ran last time, to show "what's new" once after an update.</summary>
    public string? LastRunVersion { get; set; }

    public Dictionary<string, ControllerPreferences> Controllers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>HidHide device instance ids this app put on the hide list, so they can be undone.</summary>
    public List<string> HiddenInstanceIds { get; set; } = new();

    /// <summary>HidHide's global switch was off and this app turned it on, so uninstalling may turn it off again.</summary>
    public bool HidHideActivatedByApp { get; set; }

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
    // Set when settings.json could not be read (in use, no access) or was damaged and could not be backed up:
    // saving would replace the only copy with defaults.
    private bool _doNotOverwrite;
    private bool _skipLogged;

    public SettingsStore(string? path = null)
    {
        FilePath = path ?? Path.Combine(Log.DataDirectory, "settings.json");
        Settings = Load();
    }

    public string FilePath { get; }

    /// <summary>True when settings.json was already there at startup (so this is not a fresh install).</summary>
    public bool ExistedAtStartup { get; private set; }

    /// <summary>True when settings.json is left alone this session because it could not be read or backed up safely.</summary>
    public bool ReadOnly => _doNotOverwrite;

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
        if (!File.Exists(FilePath)) return new AppSettings();
        ExistedAtStartup = true;

        string text;
        try
        {
            text = ReadText(FilePath);
        }
        catch (FileNotFoundException)
        {
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use or no access: the file itself is probably fine, so never replace it with defaults.
            _doNotOverwrite = true;
            Log.Warn("Instellingen lezen mislukt (bestand in gebruik of geen toegang); standaardinstellingen worden gebruikt en settings.json wordt niet overschreven.", ex);
            return new AppSettings();
        }

        var loaded = Parse(text, out var error);
        if (loaded != null) return loaded;

        // Damaged: start from defaults, but keep what is needed to undo HidHide later.
        var salvaged = Salvage(text);
        string kept = $"{salvaged.HiddenInstanceIds.Count} verborgen controller(s) uit het oude bestand overgenomen";
        try
        {
            File.Copy(FilePath, FilePath + ".bad", overwrite: true);
            Log.Warn($"settings.json is beschadigd; standaardinstellingen worden gebruikt ({kept}). Kopie bewaard als settings.json.bad.", error);
        }
        catch (Exception copyEx)
        {
            // Without a copy the original is the only one left: leave it alone.
            _doNotOverwrite = true;
            Log.Warn($"settings.json is beschadigd; standaardinstellingen worden gebruikt ({kept}).", error);
            Log.Warn("Kopie settings.json.bad maken mislukt; settings.json wordt daarom niet overschreven.", copyEx);
        }
        return salvaged;
    }

    /// <summary>
    /// Reads only the HidHide ownership from a settings.json (another user's, when uninstalling for all users).
    /// Never writes. Null when the file is missing or cannot be read.
    /// </summary>
    public static (List<string> HiddenInstanceIds, bool ActivatedByApp)? ReadHidHideOwnership(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = ReadText(path);
            var s = Parse(text, out _) ?? Salvage(text);
            return (s.HiddenInstanceIds, s.HidHideActivatedByApp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"{path} lezen mislukt", ex);
            return null;
        }
    }

    /// <summary>Reads the file, retrying briefly while another process (a save, a virus scanner) has it open.</summary>
    private static string ReadText(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < 5 && File.Exists(path)) { Thread.Sleep(100 * attempt); }
        }
    }

    /// <summary>The settings in <paramref name="text"/>, or null (with the reason) when the text is not valid settings JSON.</summary>
    private static AppSettings? Parse(string text, out Exception? error)
    {
        error = null;
        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(text, Json);
            if (loaded == null)
            {
                error = new InvalidDataException("settings.json bevat geen instellingen.");
                return null;
            }
            // Keep lookups case-insensitive and drop nulls from hand-edited files.
            var prefs = new Dictionary<string, ControllerPreferences>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, p) in loaded.Controllers ?? new()) if (p != null) prefs[id] = p;
            loaded.Controllers = prefs;
            loaded.HiddenInstanceIds = (loaded.HiddenInstanceIds ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            error = ex;
            return null;
        }
    }

    /// <summary>Defaults plus the HidHide ownership that can still be found in a damaged file.</summary>
    private static AppSettings Salvage(string text) => new()
    {
        HiddenInstanceIds = JsonSalvage.StringArray(text, nameof(AppSettings.HiddenInstanceIds)),
        HidHideActivatedByApp = JsonSalvage.Bool(text, nameof(AppSettings.HidHideActivatedByApp)) ?? false,
    };

    private void SaveLocked()
    {
        if (_doNotOverwrite)
        {
            if (!_skipLogged) Log.Warn("Instellingen niet opgeslagen: settings.json kon bij het starten niet veilig gelezen of bewaard worden.");
            _skipLogged = true;
            return;
        }
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
