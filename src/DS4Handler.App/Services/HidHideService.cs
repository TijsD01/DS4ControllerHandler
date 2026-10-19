using System.Collections.Concurrent;
using DS4Handler.App.Engine;
using DS4Handler.App.Infrastructure;
using DS4Handler.HidHide;

namespace DS4Handler.App.Services;

/// <summary>Whether HidHide can be used right now.</summary>
public enum HidHideAvailability
{
    Checking,
    NotInstalled,
    /// <summary>Another program (HidHide Configuration Client, DS4Windows) holds the driver open.</summary>
    Busy,
    Ready,
    Error,
}

/// <summary>What HidHide does with one controller.</summary>
public enum DeviceHideState
{
    /// <summary>Hiding is switched off (globally or for this controller).</summary>
    Off,
    Working,
    /// <summary>Games no longer see the real controller (only the virtual Xbox pad).</summary>
    Hidden,
    /// <summary>Wanted, but HidHide is not available or refused.</summary>
    Failed,
}

/// <summary>
/// Keeps HidHide in line with the settings: our own exe on the allow list, the real controllers on
/// the hide list. Every driver call runs on a worker, one at a time; the UI only reads the state.
/// </summary>
public sealed class HidHideService
{
    public const string DownloadUrl = "https://github.com/nefarius/HidHide/releases";

    private readonly SettingsStore _settings;
    private readonly ConcurrentDictionary<string, DeviceHideState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _chainGate = new();
    private Task _chain = Task.CompletedTask;
    private int _version;

    public HidHideService(SettingsStore settings) => _settings = settings;

    public HidHideAvailability Availability { get; private set; } = HidHideAvailability.Checking;

    /// <summary>A problem that stops hiding even though HidHide is installed (Dutch); null when none.</summary>
    public string? Problem { get; private set; }

    /// <summary>Changes whenever availability or a device state changes.</summary>
    public int Version => Volatile.Read(ref _version);

    public DeviceHideState StateFor(string controllerId) =>
        _states.TryGetValue(controllerId, out var s) ? s : DeviceHideState.Off;

    /// <summary>Checks the driver again (for example after the user installed HidHide or closed its configuration tool).</summary>
    public Task RefreshAsync(IEnumerable<ControllerSession> sessions)
    {
        var list = sessions.ToArray();
        return Enqueue(() =>
        {
            Problem = null;
            CheckAvailability();
            foreach (var s in list) ApplyCore(s);
        });
    }

    /// <summary>Hides or shows one controller according to the settings.</summary>
    public Task ApplyAsync(ControllerSession session) => Enqueue(() =>
    {
        if (Availability == HidHideAvailability.Checking) CheckAvailability();
        ApplyCore(session);
    });

    /// <summary>Hides or shows all given controllers according to the settings.</summary>
    public Task ApplyAllAsync(IEnumerable<ControllerSession> sessions)
    {
        var list = sessions.ToArray();
        return Enqueue(() =>
        {
            if (Availability == HidHideAvailability.Checking) CheckAvailability();
            if (!_settings.Read(s => s.HidHideEnabled)) UnhideOwnedCore();
            foreach (var s in list) ApplyCore(s);
        });
    }

    /// <summary>Makes every controller this app hid visible again (used on exit when the user wants that).</summary>
    public Task UnhideAllAsync() => Enqueue(UnhideOwnedCore);

    private Task Enqueue(Action work)
    {
        lock (_chainGate)
        {
            _chain = _chain.ContinueWith(_ =>
            {
                try { work(); }
                catch (Exception ex) { Log.Error("HidHide-actie mislukt", ex); }
                finally { Interlocked.Increment(ref _version); }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return _chain;
        }
    }

    private void CheckAvailability()
    {
        var before = Availability;
        try
        {
            Availability = HidHideClient.GetStatus() switch
            {
                HidHideStatus.Ready => HidHideAvailability.Ready,
                HidHideStatus.Busy => HidHideAvailability.Busy,
                _ => HidHideAvailability.NotInstalled,
            };
        }
        catch (Exception ex)
        {
            Log.Warn("HidHide-status opvragen mislukt", ex);
            Availability = HidHideAvailability.Error;
        }
        if (Availability != before) Log.Info($"HidHide: {Availability}");
    }

    private void ApplyCore(ControllerSession session)
    {
        bool wanted = _settings.Read(s => s.HidHideEnabled && s.For(session.Id).HideFromGames);
        var instanceId = HidHideClient.InstanceIdFromHidPath(session.DevicePath);

        if (!wanted)
        {
            if (instanceId != null && Owned(instanceId) && Availability == HidHideAvailability.Ready)
            {
                _states[session.Id] = DeviceHideState.Working;
                if (TryUnhide(instanceId)) Forget(instanceId);
            }
            _states[session.Id] = DeviceHideState.Off;
            return;
        }

        if (Availability != HidHideAvailability.Ready || instanceId == null)
        {
            if (instanceId == null) Log.Warn($"Geen apparaat-id voor HidHide uit {session.DevicePath}");
            _states[session.Id] = DeviceHideState.Failed;
            return;
        }

        _states[session.Id] = DeviceHideState.Working;
        // Never hide a pad before our own exe is allowed through, or we would lock ourselves out on the next reconnect.
        if (!EnsureWhitelisted())
        {
            _states[session.Id] = DeviceHideState.Failed;
            return;
        }

        try
        {
            var client = new HidHideClient();
            if (!client.IsActive)
            {
                // HidHide's global switch: it also applies to devices hidden by the user or other tools.
                Log.Info("HidHide-verbergen (algemene schakelaar) aangezet.");
                client.SetActive(true);
                // Remembered so uninstalling can turn it off again (only then, and only if nothing else is hidden).
                _settings.Update(s => s.HidHideActivatedByApp = true);
            }
            // Only take ownership when we put it on the list (or did so before), so we never unhide a
            // pad the user hid themselves.
            if (client.HideDevice(instanceId) || Owned(instanceId)) Remember(instanceId);
            _states[session.Id] = DeviceHideState.Hidden;
        }
        catch (Exception ex)
        {
            Log.Warn($"Controller {session.Id} verbergen mislukt", ex);
            _states[session.Id] = DeviceHideState.Failed;
            if (ex is HidHideException hh) Problem = Describe(hh);
            CheckAvailability(); // probably became busy
        }
    }

    private bool EnsureWhitelisted()
    {
        // Checked on every hide (cheap): the user may have edited HidHide's list in the meantime.
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || HidHideClient.IsSharedHostExecutable(exe))
        {
            // Allowing dotnet.exe would let every .NET program see the hidden pads.
            Problem = "De app draait via dotnet.exe. Start DS4Handler.exe zelf, anders kan HidHide de app niet als uitzondering toevoegen en blijft de controller zichtbaar.";
            return false;
        }
        try
        {
            var result = new HidHideClient().EnsureApplicationWhitelisted(exe);
            switch (result)
            {
                case WhitelistResult.Added:
                case WhitelistResult.AlreadyPresent:
                case WhitelistResult.SkippedInvertedMode: // inverted list = deny list: we are allowed already
                    Problem = null;
                    return true;
                case WhitelistResult.RefusedSharedHost:
                    Problem = "De app draait via dotnet.exe. Start DS4Handler.exe zelf, anders kan HidHide de app niet als uitzondering toevoegen en blijft de controller zichtbaar.";
                    return false;
                default: // PathConversionFailed
                    Problem = "De map waar DS4 Handler staat kon niet aan HidHide worden doorgegeven (bijvoorbeeld een netwerkschijf). Zet de app op een lokale schijf.";
                    return false;
            }
        }
        catch (HidHideException ex)
        {
            Log.Warn("DS4Handler aan de HidHide-uitzonderingen toevoegen mislukt", ex);
            Problem = Describe(ex);
            CheckAvailability();
            return false;
        }
    }

    /// <summary>Plain Dutch explanation of a HidHide error.</summary>
    public static string Describe(HidHideException ex) => ex.Kind switch
    {
        HidHideErrorKind.NotInstalled => "HidHide is niet geïnstalleerd.",
        HidHideErrorKind.Busy => "HidHide is bezet: een ander programma (zoals HidHide Configuration Client of DS4Windows) heeft het open. Sluit dat programma en klik op Opnieuw controleren.",
        _ => "HidHide gaf een fout: " + ex.Message,
    };

    private void UnhideOwnedCore()
    {
        var owned = _settings.Read(s => s.HiddenInstanceIds.ToArray());
        if (owned.Length == 0) return;
        if (Availability != HidHideAvailability.Ready) CheckAvailability();
        if (Availability != HidHideAvailability.Ready)
        {
            Log.Warn($"HidHide is niet beschikbaar ({Availability}); {owned.Length} controller(s) blijven verborgen.");
            return;
        }
        int failed = 0;
        foreach (var id in owned)
            if (TryUnhide(id)) Forget(id);
            else failed++;
        Log.Info(failed == 0
            ? $"{owned.Length} controller(s) zichtbaar gemaakt."
            : $"{owned.Length - failed} van {owned.Length} controller(s) zichtbaar gemaakt; {failed} blijven verborgen.");
        foreach (var key in _states.Keys) _states[key] = DeviceHideState.Off;
    }

    private bool TryUnhide(string instanceId)
    {
        try
        {
            new HidHideClient().UnhideDevice(instanceId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Zichtbaar maken van {instanceId} mislukt", ex);
            return false;
        }
    }

    private bool Owned(string instanceId) =>
        _settings.Read(s => s.HiddenInstanceIds.Contains(instanceId, StringComparer.OrdinalIgnoreCase));

    private void Remember(string instanceId) => _settings.Update(s =>
    {
        if (!s.HiddenInstanceIds.Contains(instanceId, StringComparer.OrdinalIgnoreCase)) s.HiddenInstanceIds.Add(instanceId);
    });

    private void Forget(string instanceId) => _settings.Update(s =>
        s.HiddenInstanceIds.RemoveAll(x => string.Equals(x, instanceId, StringComparison.OrdinalIgnoreCase)));
}
