using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using DS4Handler.Core.Calibration;
using DS4Handler.Core.Tuning;
using DS4Handler.Core.Input;

namespace DS4Handler.Core.Profiles;

/// <summary>Helpers around controller ids: the app uses "ds4-" / "dualsense-" plus 12 hex digits of the MAC.</summary>
public static class ControllerIds
{
    /// <summary>Marker of an id that hangs on a USB port because no MAC could be read.</summary>
    public const string PathFallbackMarker = "-path-";

    /// <summary>Longest id the app makes ("dualsense-path-" + 16 hex digits).</summary>
    public const int MaxLength = 31;

    private static readonly Regex Canonical = new(
        "^(ds4|dualsense)-([0-9a-f]{12}|path-[0-9a-f]{16})$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// True for an id exactly as the app builds it: "ds4-"/"dualsense-" plus 12 lowercase hex digits of
    /// the MAC, or the path fallback "ds4-path-" plus 16 lowercase hex digits.
    /// </summary>
    public static bool IsCanonical(string? controllerId) =>
        controllerId is { Length: <= MaxLength } && Canonical.IsMatch(controllerId);

    /// <summary>True for an id built from the device path (no MAC found).</summary>
    public static bool IsPathFallback(string? controllerId) =>
        controllerId != null && controllerId.Contains(PathFallbackMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>"ds4-a4538512abcd" → "A4:53:85:12:AB:CD"; null when the id holds no MAC.</summary>
    public static string? MacFromId(string? controllerId)
    {
        if (string.IsNullOrWhiteSpace(controllerId) || IsPathFallback(controllerId)) return null;
        int dash = controllerId.LastIndexOf('-');
        var hex = new StringBuilder(12);
        foreach (char c in controllerId[(dash + 1)..])
        {
            if (char.IsAsciiHexDigit(c)) hex.Append(c);
            else if (c is not (':' or '-')) return null;
        }
        return hex.Length == 12 ? FormatMac(hex.ToString()) : null;
    }

    /// <summary>"a4538512abcd" → "A4:53:85:12:AB:CD".</summary>
    public static string FormatMac(string hex12)
    {
        if (hex12 is null || hex12.Length != 12) throw new ArgumentException("Expected 12 hex digits.", nameof(hex12));
        var sb = new StringBuilder(17);
        for (int i = 0; i < 12; i += 2)
        {
            if (i > 0) sb.Append(':');
            sb.Append(char.ToUpperInvariant(hex12[i])).Append(char.ToUpperInvariant(hex12[i + 1]));
        }
        return sb.ToString();
    }

    /// <summary>The model a controller id belongs to, from its prefix; null when unknown.</summary>
    public static ControllerModel? ModelFromId(string? controllerId)
    {
        if (controllerId == null) return null;
        if (controllerId.StartsWith("ds4-", StringComparison.OrdinalIgnoreCase)) return ControllerModel.DualShock4;
        if (controllerId.StartsWith("dualsense-", StringComparison.OrdinalIgnoreCase)) return ControllerModel.DualSense;
        return null;
    }
}

/// <summary>A profile read from an exported file, with the friendly name it was exported with.</summary>
public sealed record ImportedProfile(CalibrationProfile Profile, string? Name);

/// <summary>
/// Copying profiles between controllers and exporting / importing them as a JSON file, so a friend
/// can bring the calibration of his controller.
/// </summary>
public static class ProfileTransfer
{
    public const string FormatName = "DS4Handler.Profile";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class Envelope
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public string? Name { get; set; }
        public CalibrationProfile? Profile { get; set; }
    }

    /// <summary>
    /// A deep copy of <paramref name="source"/>'s learned calibration and tuning for another controller: only the
    /// id, the model and the time change. The source is left untouched.
    /// </summary>
    public static CalibrationProfile CopyFor(CalibrationProfile source, string targetId, ControllerModel targetModel)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("Target id is required.", nameof(targetId));
        // A JSON round trip copies every array and any field added later, so nothing is shared.
        var copy = JsonSerializer.Deserialize<CalibrationProfile>(JsonSerializer.Serialize(source, Json), Json)!;
        return Normalize(copy) with { ControllerId = targetId, Model = targetModel, UpdatedUtc = DateTime.UtcNow };
    }

    /// <summary>The export file content.</summary>
    public static string Serialize(CalibrationProfile profile, string? name)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.Serialize(new Envelope
        {
            Format = FormatName,
            Version = FormatVersion,
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Profile = profile,
        }, Json);
    }

    /// <summary>
    /// Reads an export file, or a plain profile file from the profiles folder.
    /// Throws <see cref="FormatException"/> when the content is not a usable profile.
    /// </summary>
    public static ImportedProfile Deserialize(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new FormatException("The file is not valid JSON.", ex); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("The file holds no profile.");
            try
            {
                CalibrationProfile? profile;
                string? name = null;
                if (doc.RootElement.TryGetProperty(nameof(Envelope.Format), out _))
                {
                    var envelope = doc.RootElement.Deserialize<Envelope>(Json);
                    if (envelope?.Format != FormatName) throw new FormatException("This is not a DS4 Handler profile.");
                    if (envelope.Version > FormatVersion) throw new FormatException("The profile was made by a newer version of DS4 Handler.");
                    profile = envelope.Profile;
                    name = string.IsNullOrWhiteSpace(envelope.Name) ? null : envelope.Name.Trim();
                }
                else
                {
                    profile = doc.RootElement.Deserialize<CalibrationProfile>(Json);
                }

                if (profile is null || string.IsNullOrWhiteSpace(profile.ControllerId))
                    throw new FormatException("The profile has no controller id.");
                if (!Enum.IsDefined(profile.Model)) throw new FormatException("Unknown controller model.");

                // The id becomes a file name and a settings key: accept only ids the app itself makes.
                var id = profile.ControllerId.Trim().ToLowerInvariant();
                if (!ControllerIds.IsCanonical(id)) throw new FormatException("The controller id is not one DS4 Handler makes.");
                if (ControllerIds.ModelFromId(id) != profile.Model) throw new FormatException("The controller id does not match the model.");
                if (name is { Length: > 60 }) name = name[..60];
                return new ImportedProfile(Normalize(profile) with { ControllerId = id }, name);
            }
            catch (JsonException ex)
            {
                throw new FormatException("The profile could not be read.", ex);
            }
        }
    }

    /// <summary>Replaces sections that were null in the JSON with defaults.</summary>
    private static CalibrationProfile Normalize(CalibrationProfile p) => p with
    {
        ControllerId = (p.ControllerId ?? "").Trim(),
        LeftStick = p.LeftStick ?? new StickCalibrationData(),
        RightStick = p.RightStick ?? new StickCalibrationData(),
        L2 = p.L2 ?? new TriggerCalibrationData(),
        R2 = p.R2 ?? new TriggerCalibrationData(),
        Tuning = InputShaper.Sanitize(p.Tuning), // missing or "Tuning": null → defaults
    };
}
