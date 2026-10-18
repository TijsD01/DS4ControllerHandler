using DS4Handler.App.Engine;
using DS4Handler.App.Infrastructure;
using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;

namespace DS4Handler.App.Services;

/// <summary>One known controller profile, as the "Profielen" tab shows it.</summary>
public sealed record ProfileInfo(
    string Id,
    string? Name,
    ControllerModel? Model,
    string? Mac,
    DateTime? LastSeenUtc,
    // Saved calibration; null when nothing was saved yet.
    CalibrationProfile? Profile,
    // The controller whose own profile this is, when it is connected now.
    ControllerSession? Connected,
    // Connected controllers that learn into this profile (own or linked).
    IReadOnlyList<ControllerSession> UsedBy);

/// <summary>
/// Everything around saved profiles that the user can do: names, last seen, linking a controller to
/// a shared profile, copy, export, import and delete. Methods do file I/O: call them from a worker.
/// The file key of a profile is always the controller id (MAC); names live in the settings.
/// </summary>
public sealed class ProfileLibrary
{
    private readonly ProfileStore _store;
    private readonly SettingsStore _settings;
    private readonly ControllerManager _manager;
    private readonly object _gate = new(); // one profile operation at a time

    public ProfileLibrary(ProfileStore store, SettingsStore settings, ControllerManager manager)
    {
        _store = store;
        _settings = settings;
        _manager = manager;
        manager.ProfileLinkFor = id => settings.Read(s => s.Controllers.TryGetValue(id, out var p) ? p.LinkedProfileId : null);
        manager.SessionStarted += session => MarkSeen(new[] { session });
    }

    /// <summary>Friendly name, or the model name when none was chosen.</summary>
    public string DisplayName(string id)
    {
        var name = _settings.Read(s => s.Controllers.TryGetValue(id, out var p) ? p.Name : null);
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return ControllerIds.ModelFromId(id) switch
        {
            ControllerModel.DualShock4 => "DualShock 4",
            ControllerModel.DualSense => "DualSense",
            _ => "Controller",
        };
    }

    /// <summary>All profiles: saved files, controllers known from the settings and the connected ones.</summary>
    public IReadOnlyList<ProfileInfo> Load()
    {
        var sessions = _manager.Sessions;
        var saved = new Dictionary<string, CalibrationProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _store.LoadAll())
            if (!string.IsNullOrWhiteSpace(p.ControllerId)) saved[p.ControllerId] = p;

        var ids = new HashSet<string>(saved.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var id in _settings.Read(s => s.Controllers.Keys.ToArray()))
            if (ControllerIds.ModelFromId(id) != null) ids.Add(id);
        foreach (var s in sessions)
        {
            ids.Add(s.Id);
            ids.Add(s.ProfileId);
        }

        var result = new List<ProfileInfo>();
        foreach (var id in ids)
        {
            saved.TryGetValue(id, out var profile);
            var prefs = _settings.Read(s => s.Controllers.TryGetValue(id, out var p) ? (p.Name, p.LastSeenUtc) : (null, null));
            var connected = sessions.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            var usedBy = sessions.Where(s => string.Equals(s.ProfileId, id, StringComparison.OrdinalIgnoreCase) && !s.ProfileBlocked).ToArray();
            var model = connected?.Model ?? profile?.Model ?? ControllerIds.ModelFromId(id);
            result.Add(new ProfileInfo(id, prefs.Name, model, ControllerIds.MacFromId(id), prefs.LastSeenUtc, profile, connected, usedBy));
        }

        // Connected first, then most recently seen, then by name.
        return result
            .OrderByDescending(p => p.Connected != null)
            .ThenByDescending(p => p.LastSeenUtc ?? DateTime.MinValue)
            .ThenBy(p => DisplayName(p.Id), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Sets the friendly name; empty clears it. The file key never changes.</summary>
    public void Rename(string id, string? name)
    {
        var clean = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (clean is { Length: > 60 }) clean = clean[..60];
        _settings.Update(s => s.For(id).Name = clean);
    }

    public void MarkSeen(IEnumerable<ControllerSession> sessions)
    {
        var now = DateTime.UtcNow;
        var ids = sessions.Select(s => s.Id).ToArray();
        if (ids.Length == 0) return;
        _settings.Update(s =>
        {
            foreach (var id in ids) s.For(id).LastSeenUtc = now;
        });
    }

    /// <summary>
    /// Links a connected controller to another profile, or back to its own (null or its own id).
    /// Returns a Dutch reason when refused, null when done.
    /// </summary>
    public string? Link(ControllerSession session, string? profileId)
    {
        string target = string.IsNullOrWhiteSpace(profileId) ? session.Id : profileId;
        lock (_gate)
        {
            var targetModel = ModelOf(target);
            if (targetModel != null && targetModel != session.Model)
                return "Dat profiel hoort bij een ander type controller. Kies een profiel van een " + session.DisplayName + ", of kopieer de kalibratie.";
            if (!_manager.TryLinkProfile(session, target))
                return "Dat profiel wordt al gebruikt door een andere aangesloten controller. Twee controllers kunnen niet tegelijk in hetzelfde profiel leren.";
            _settings.Update(s => s.For(session.Id).LinkedProfileId =
                string.Equals(target, session.Id, StringComparison.OrdinalIgnoreCase) ? null : target);
            return null;
        }
    }

    /// <summary>Copies the learned calibration of one profile onto another. Returns a Dutch error, or null when done.</summary>
    public string? Copy(string sourceId, string targetId)
    {
        if (string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase)) return "Bron en doel zijn hetzelfde profiel.";
        lock (_gate)
        {
            var source = CurrentProfile(sourceId);
            if (source == null) return "Er is voor dit profiel nog niets geleerd om te kopiëren.";
            var model = ModelOf(targetId) ?? source.Model;
            var copy = ProfileTransfer.CopyFor(source, targetId, model);
            return Write(copy);
        }
    }

    /// <summary>Writes the profile of <paramref name="id"/> to an export file. Returns a Dutch error, or null when done.</summary>
    public string? Export(string id, string path)
    {
        lock (_gate)
        {
            var profile = CurrentProfile(id);
            if (profile == null) return "Er is voor dit profiel nog niets geleerd om te exporteren.";
            try
            {
                File.WriteAllText(path, ProfileTransfer.Serialize(profile, NameOrNull(id)));
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn($"Profiel {id} exporteren naar {path} mislukt", ex);
                return "Het bestand kon niet worden opgeslagen: " + ex.Message;
            }
        }
    }

    /// <summary>Reads an export file without changing anything. Throws <see cref="FormatException"/> or I/O errors.</summary>
    public static ImportedProfile Peek(string path) => ProfileTransfer.Deserialize(File.ReadAllText(path));

    /// <summary>True when a profile with this id is saved or known.</summary>
    public bool Exists(string id) =>
        _store.Load(id) != null || _manager.IsProfileInUse(id);

    /// <summary>Saves an imported profile under its own controller id. Returns a Dutch error, or null when done.</summary>
    public string? Import(ImportedProfile imported)
    {
        lock (_gate)
        {
            var error = Write(imported.Profile);
            if (error == null && imported.Name != null)
            {
                var id = imported.Profile.ControllerId;
                _settings.Update(s =>
                {
                    var prefs = s.For(id);
                    if (string.IsNullOrWhiteSpace(prefs.Name)) prefs.Name = imported.Name;
                });
            }
            return error;
        }
    }

    /// <summary>Deletes a profile that no connected controller uses. Returns a Dutch error, or null when done.</summary>
    public string? Delete(string id)
    {
        lock (_gate)
        {
            if (_manager.IsProfileInUse(id) || _manager.Sessions.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
                return "Dit profiel is in gebruik door een aangesloten controller. Koppel die eerst los.";
            try
            {
                _store.Delete(id);
            }
            catch (Exception ex)
            {
                Log.Warn($"Profiel {id} verwijderen mislukt", ex);
                return "Het profiel kon niet worden verwijderd: " + ex.Message;
            }
            _settings.Update(s =>
            {
                s.Controllers.Remove(id);
                foreach (var prefs in s.Controllers.Values)
                    if (string.Equals(prefs.LinkedProfileId, id, StringComparison.OrdinalIgnoreCase)) prefs.LinkedProfileId = null;
            });
            Log.Info($"Profiel {id} verwijderd.");
            return null;
        }
    }

    /// <summary>The newest calibration of a profile: a connected controller saves first, so nothing learned is missed.</summary>
    private CalibrationProfile? CurrentProfile(string id)
    {
        UserOf(id)?.SaveProfile();
        return _store.Load(id);
    }

    /// <summary>Stores a profile; when a connected controller learns into it, that controller takes it over live.</summary>
    private string? Write(CalibrationProfile profile)
    {
        try
        {
            if (UserOf(profile.ControllerId) is { } session)
            {
                if (session.Model != profile.Model)
                    profile = ProfileTransfer.CopyFor(profile, profile.ControllerId, session.Model);
                session.ReplaceCalibration(profile);
            }
            else
            {
                _store.Save(profile);
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Profiel {profile.ControllerId} opslaan mislukt", ex);
            return "Het profiel kon niet worden opgeslagen: " + ex.Message;
        }
    }

    private ControllerSession? UserOf(string profileId) => _manager.Sessions.FirstOrDefault(s =>
        string.Equals(s.ProfileId, profileId, StringComparison.OrdinalIgnoreCase) && !s.ProfileBlocked);

    private ControllerModel? ModelOf(string id) =>
        _manager.Sessions.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))?.Model
        ?? SafeLoad(id)?.Model
        ?? ControllerIds.ModelFromId(id);

    private CalibrationProfile? SafeLoad(string id)
    {
        try { return _store.Load(id); } catch { return null; }
    }

    private string? NameOrNull(string id) =>
        _settings.Read(s => s.Controllers.TryGetValue(id, out var p) ? p.Name : null);
}
