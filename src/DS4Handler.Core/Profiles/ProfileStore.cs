using DS4Handler.Core.Calibration;
using DS4Handler.Core.Tuning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DS4Handler.Core.Profiles;

/// <summary>Saves and loads <see cref="CalibrationProfile"/>s as one JSON file per controller.</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public ProfileStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Directory is required.", nameof(directory));
        _directory = directory;
    }

    /// <summary>%APPDATA%/DS4ControllerHandler/profiles (or the platform equivalent).</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DS4ControllerHandler",
        "profiles");

    public string Directory => _directory;

    /// <summary>
    /// The saved profile, or null when there is none. A file that exists but cannot be read also gives
    /// null; use <see cref="LoadStrict"/> where that difference matters (before saving over it).
    /// </summary>
    public CalibrationProfile? Load(string controllerId)
    {
        var path = PathFor(controllerId);
        return File.Exists(path) ? ReadFile(path) : null;
    }

    /// <summary>
    /// The saved profile, or null only when no file exists. A file that exists but cannot be read or
    /// parsed throws <see cref="InvalidDataException"/> (inner exception: the cause), so a caller never
    /// mistakes a damaged or locked profile for a missing one and overwrites what was learned.
    /// </summary>
    public CalibrationProfile? LoadStrict(string controllerId)
    {
        var path = PathFor(controllerId);
        if (!File.Exists(path)) return null;
        try
        {
            return ReadFileCore(path) ?? throw new InvalidDataException($"Profile file {path} holds no profile.");
        }
        catch (Exception e) when (IsReadError(e))
        {
            throw new InvalidDataException($"Profile file {path} could not be read.", e);
        }
    }

    /// <summary>True when a file exists for this profile, readable or not.</summary>
    public bool Exists(string controllerId) => File.Exists(PathFor(controllerId));

    public void Save(CalibrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        System.IO.Directory.CreateDirectory(_directory);
        var path = PathFor(profile.ControllerId);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(profile, JsonOptions), Encoding.UTF8);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public IReadOnlyList<CalibrationProfile> LoadAll()
    {
        if (!System.IO.Directory.Exists(_directory)) return Array.Empty<CalibrationProfile>();
        var result = new List<CalibrationProfile>();
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var profile = ReadFile(file);
            if (profile is not null) result.Add(profile);
        }
        return result;
    }

    public bool Delete(string controllerId)
    {
        var path = PathFor(controllerId);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static CalibrationProfile? ReadFile(string path)
    {
        try
        {
            return ReadFileCore(path);
        }
        catch (Exception e) when (IsReadError(e))
        {
            return null;
        }
    }

    private static CalibrationProfile? ReadFileCore(string path)
    {
        var profile = JsonSerializer.Deserialize<CalibrationProfile>(File.ReadAllText(path), JsonOptions);
        return profile is null ? null : Normalize(profile);
    }

    private static bool IsReadError(Exception e) =>
        e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException;

    /// <summary>Replaces sections that were null in the JSON (for example "L2": null) with defaults and sanitizes the tuning.</summary>
    private static CalibrationProfile Normalize(CalibrationProfile p) => p with
    {
        ControllerId = p.ControllerId ?? "",
        LeftStick = NormalizeStick(p.LeftStick),
        RightStick = NormalizeStick(p.RightStick),
        L2 = p.L2 ?? new TriggerCalibrationData(),
        R2 = p.R2 ?? new TriggerCalibrationData(),
        Tuning = InputShaper.Sanitize(p.Tuning), // missing or "Tuning": null → defaults
    };

    private static StickCalibrationData NormalizeStick(StickCalibrationData? d) =>
        d is null ? new StickCalibrationData() : d.OuterRadii is null ? d with { OuterRadii = Array.Empty<float>() } : d;

    private string PathFor(string controllerId) => Path.Combine(_directory, SanitizeId(controllerId) + ".json");

    /// <summary>
    /// Turns a controller id (often a MAC address like "AA:BB:CC:DD:EE:FF") into a safe file name.
    /// Letters, digits, '-' and '_' are kept; ':' becomes '-'; anything else becomes '_'.
    /// </summary>
    public static string SanitizeId(string controllerId)
    {
        if (string.IsNullOrWhiteSpace(controllerId)) return "unknown";
        var sb = new StringBuilder(controllerId.Length);
        foreach (var c in controllerId.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_') sb.Append(char.ToLowerInvariant(c));
            else if (c == ':') sb.Append('-');
            else sb.Append('_');
        }
        var name = sb.ToString();
        // Windows refuses device names such as CON or COM1 as file names, even with an extension.
        var upper = name.ToUpperInvariant();
        bool reserved = upper is "CON" or "PRN" or "AUX" or "NUL"
            || (upper.Length == 4 && (upper.StartsWith("COM") || upper.StartsWith("LPT")) && char.IsAsciiDigit(upper[3]));
        return reserved ? "_" + name : name;
    }
}
