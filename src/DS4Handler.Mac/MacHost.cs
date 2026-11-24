using DS4Handler.App.Engine;
using DS4Handler.App.Infrastructure;
using DS4Handler.App.Services;
using DS4Handler.Core.Profiles;

namespace DS4Handler.Mac;

/// <summary>
/// The engine for the Mac app: the same controller manager, profiles and settings as on Windows, minus the
/// Windows-only parts (ViGEmBus, HidHide, start with Windows, updates).
/// </summary>
public sealed class MacHost : IDisposable
{
    private bool _disposed;

    public MacHost()
    {
        SettingsStore = new SettingsStore();
        Profiles = new ProfileStore(ProfileStore.DefaultDirectory);
        Manager = new ControllerManager(Profiles, id => SettingsStore.Read(s => (s.For(id).LearningEnabled, s.For(id).Output, s.For(id).JitterFilterEnabled)));
    }

    public SettingsStore SettingsStore { get; }
    public ProfileStore Profiles { get; }
    public ControllerManager Manager { get; }

    public void Start() => Manager.Start();

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

    /// <summary>Opens a folder in Finder.</summary>
    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start("open", $"\"{folder}\"");
        }
        catch (Exception ex) { Log.Warn($"Openen van {folder} mislukt", ex); }
    }

    /// <summary>Saves every profile and the settings.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Manager.Dispose();
        SettingsStore.Save();
        Log.Info("Afgesloten.");
    }
}
