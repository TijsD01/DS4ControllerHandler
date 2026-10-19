using System.Diagnostics;
using DS4Handler.App.Engine;
using DS4Handler.App.Infrastructure;
using DS4Handler.Core.Profiles;
using DS4Handler.Core.Tuning;

namespace DS4Handler.App.Services;

/// <summary>
/// Wires the engine and the services together and owns their lifetime. The view models talk to
/// this class; nothing here touches WPF.
/// </summary>
public sealed class AppHost : IDisposable
{
    public const string ViGEmDownloadUrl = "https://github.com/nefarius/ViGEmBus/releases";

    private bool _disposed;

    public AppHost()
    {
        SettingsStore = new SettingsStore();
        Profiles = new ProfileStore(ProfileStore.DefaultDirectory);
        Manager = new ControllerManager(Profiles, id => SettingsStore.Read(s => (s.For(id).LearningEnabled, s.For(id).Output, s.For(id).JitterFilterEnabled)));
        HidHide = new HidHideService(SettingsStore);
        Manager.SessionStarted += session => HidHide.ApplyAsync(session);
        Library = new ProfileLibrary(Profiles, SettingsStore, Manager);
    }

    public SettingsStore SettingsStore { get; }

    /// <summary>The "Profielen" tab: names, linking, copy, export / import of profiles.</summary>
    public ProfileLibrary Library { get; }
    public AppSettings Settings => SettingsStore.Settings;
    public ProfileStore Profiles { get; }
    public ControllerManager Manager { get; }
    public HidHideService HidHide { get; }

    public void Start()
    {
        Manager.Start();
        _ = HidHide.RefreshAsync(Array.Empty<ControllerSession>());
    }

    public void SetLearning(ControllerSession session, bool enabled)
    {
        session.LearningEnabled = enabled;
        SettingsStore.Update(s => s.For(session.Id).LearningEnabled = enabled);
    }

    public void SetJitterFilter(ControllerSession session, bool enabled)
    {
        session.JitterFilterEnabled = enabled;
        SettingsStore.Update(s => s.For(session.Id).JitterFilterEnabled = enabled);
    }

    /// <summary>
    /// Uses new user tuning at once and saves it in the controller's profile (done by the session).
    /// False when the profile's tuning was replaced since <paramref name="generation"/> was read (see
    /// <see cref="ControllerSession.TuningGeneration"/>): nothing changed; reload from the session.
    /// </summary>
    public bool SetTuning(ControllerSession session, InputTuning tuning, int generation) => session.ApplyTuning(tuning, generation);

    /// <summary>Learning on/off for every connected controller (tray menu).</summary>
    public void SetLearningAll(bool enabled)
    {
        foreach (var s in Manager.Sessions) SetLearning(s, enabled);
    }

    public void SetHideFromGames(ControllerSession session, bool hide)
    {
        SettingsStore.Update(s => s.For(session.Id).HideFromGames = hide);
        _ = HidHide.ApplyAsync(session);
    }

    /// <summary>Switches between a virtual Xbox 360 and DualShock 4 controller (driver calls run on a worker).</summary>
    public void SetOutputMode(ControllerSession session, OutputMode mode)
    {
        SettingsStore.Update(s => s.For(session.Id).Output = mode);
        session.RequestOutputMode(mode);
    }

    public void SetHidHideEnabled(bool enabled)
    {
        SettingsStore.Update(s => s.HidHideEnabled = enabled);
        _ = HidHide.ApplyAllAsync(Manager.Sessions);
    }

    public void SetStartWithWindows(bool enabled)
    {
        SettingsStore.Update(s => s.StartWithWindows = enabled);
        ThreadPool.QueueUserWorkItem(_ => StartupRegistration.Apply(enabled));
    }

    public void RecheckHidHide() => _ = HidHide.RefreshAsync(Manager.Sessions);

    public static void OpenUrl(string url) => Shell(url);

    public static void OpenFolder(string folder)
    {
        try { Directory.CreateDirectory(folder); } catch { }
        Shell(folder);
    }

    private static void Shell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Openen van {target} mislukt", ex); }
    }

    /// <summary>Saves everything, unplugs the virtual controllers and (if wanted) unhides the real ones.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Library.MarkSeen(Manager.Sessions);
        Manager.Dispose(); // saves all profiles
        if (SettingsStore.Read(s => s.UnhideOnExit))
        {
            try
            {
                if (!HidHide.UnhideAllAsync().Wait(TimeSpan.FromSeconds(3)))
                    Log.Warn("Controllers zichtbaar maken bij afsluiten duurde te lang; mogelijk blijven ze verborgen.");
            }
            catch (Exception ex) { Log.Warn("Controllers zichtbaar maken bij afsluiten mislukt", ex); }
        }
        SettingsStore.Save();
        Log.Info("Afgesloten.");
    }
}
