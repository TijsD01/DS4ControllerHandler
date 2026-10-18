using System.Windows.Input;
using DS4Handler.App.Infrastructure;
using DS4Handler.App.Services;
using DS4Handler.Core.Updates;

namespace DS4Handler.App.ViewModels;

/// <summary>
/// Update banner and the Updates block in Settings: "new version available" with what's new,
/// download + install, and once after an update "this is new". Lives on the UI thread.
/// </summary>
public sealed class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);
    private const int MaxNotesInBanner = 6;

    private readonly AppHost _host;
    private readonly UpdateService _service = new();
    private DateTime _nextCheck = DateTime.UtcNow + FirstCheckDelay;
    private UpdateInfo? _available;
    private bool _availableDismissed;
    private bool _checking;
    private bool _downloading;
    private double _progress;
    private string? _error;
    private string _status = "Nog niet gezocht.";
    private (ReleaseVersion Version, IReadOnlyList<string> Notes)? _justUpdated;

    public UpdateViewModel(AppHost host)
    {
        _host = host;
        CheckNowCommand = new RelayCommand(() => _ = CheckAsync(manual: true), () => !_checking && !_downloading);
        DetectJustUpdated();
    }

    /// <summary>Changes whenever the banners change, so the main view model knows to rebuild them.</summary>
    public int Version { get; private set; }

    public ICommand CheckNowCommand { get; }

    public string CurrentVersionText => "Versie " + UpdateService.CurrentVersion;

    public string Status { get => _status; private set => SetField(ref _status, value); }

    public bool CheckForUpdates
    {
        get => _host.SettingsStore.Read(s => s.CheckForUpdates);
        set
        {
            _host.SettingsStore.Update(s => s.CheckForUpdates = value);
            if (value) _nextCheck = DateTime.UtcNow;
            OnPropertyChanged();
        }
    }

    /// <summary>Called about once a second.</summary>
    public void Tick()
    {
        if (_checking || _downloading || DateTime.UtcNow < _nextCheck) return;
        if (!CheckForUpdates) { _nextCheck = DateTime.UtcNow + CheckInterval; return; }
        _ = CheckAsync(manual: false);
    }

    public IEnumerable<NoticeViewModel> Notices()
    {
        if (_justUpdated is { } done)
        {
            yield return new NoticeViewModel
            {
                Level = Level.Ok,
                Title = $"DS4 Handler is bijgewerkt naar versie {done.Version}",
                Text = done.Notes.Count > 0
                    ? "Nieuw in deze versie:\n" + Bullets(done.Notes) + "\nJe profielen en instellingen zijn bewaard gebleven."
                    : "Je profielen en instellingen zijn bewaard gebleven.",
                PrimaryText = "Sluiten",
                PrimaryCommand = new RelayCommand(() => { _justUpdated = null; Changed(); }),
                PrimaryTip = "Verberg deze melding.",
            };
        }

        if (_available is { } update && !_availableDismissed)
        {
            var notes = update.AllNotes;
            string whatsNew = notes.Count > 0 ? "\n\nNieuw:\n" + Bullets(notes) : "";
            if (_downloading)
            {
                yield return new NoticeViewModel
                {
                    Level = Level.Learning,
                    Title = $"Versie {update.Latest.Version} wordt gedownload… {Math.Round(_progress * 100):0}%",
                    Text = "Daarna sluit de app even en wordt de nieuwe versie geïnstalleerd. Je profielen en instellingen blijven bewaard.",
                };
            }
            else if (UpdateService.IsInstalled)
            {
                yield return new NoticeViewModel
                {
                    Level = Level.Learning,
                    Title = $"Nieuwe versie beschikbaar: {update.Latest.Version}",
                    Text = (_error != null ? "Bijwerken lukte niet: " + _error + "\n\n" : "")
                           + $"Je hebt nu versie {UpdateService.CurrentVersion}. Bijwerken duurt ongeveer een minuut; de app sluit even en start daarna vanzelf opnieuw. Je profielen en instellingen blijven bewaard."
                           + whatsNew,
                    PrimaryText = _error != null ? "Opnieuw proberen" : "Nu bijwerken",
                    PrimaryCommand = new RelayCommand(() => _ = InstallAsync(update.Latest)),
                    PrimaryTip = Tips.UpdateNow,
                    SecondaryText = "Later",
                    SecondaryCommand = new RelayCommand(() => { _availableDismissed = true; Changed(); }),
                    SecondaryTip = Tips.UpdateLater,
                };
            }
            else
            {
                // Portable exe: there is no installation to upgrade, so point to the download.
                yield return new NoticeViewModel
                {
                    Level = Level.Learning,
                    Title = $"Nieuwe versie beschikbaar: {update.Latest.Version}",
                    Text = $"Je gebruikt de losse (portable) versie {UpdateService.CurrentVersion}. Download de nieuwe versie en vervang dit bestand, of installeer met de Setup; je profielen blijven bewaard." + whatsNew,
                    PrimaryText = "Downloadpagina openen",
                    PrimaryCommand = new RelayCommand(() => AppHost.OpenUrl(string.IsNullOrEmpty(update.Latest.PageUrl) ? UpdateCheck.ReleasesPageUrl : update.Latest.PageUrl)),
                    PrimaryTip = Tips.UpdateDownloadPage,
                    SecondaryText = "Later",
                    SecondaryCommand = new RelayCommand(() => { _availableDismissed = true; Changed(); }),
                    SecondaryTip = Tips.UpdateLater,
                };
            }
        }
    }

    private async Task CheckAsync(bool manual)
    {
        if (_checking || _downloading) return;
        _checking = true;
        Status = "Bezig met zoeken naar een nieuwe versie…";
        try
        {
            var update = await _service.CheckAsync();
            _nextCheck = DateTime.UtcNow + CheckInterval;
            if (update != null)
            {
                bool isNew = _available?.Latest.Version != update.Latest.Version;
                _available = update;
                if (isNew || manual) { _availableDismissed = false; _error = null; }
                Status = $"Versie {update.Latest.Version} is beschikbaar (je hebt {UpdateService.CurrentVersion}).";
            }
            else
            {
                Status = $"Je hebt de nieuwste versie. Laatst gezocht om {DateTime.Now:HH:mm}.";
            }
        }
        finally
        {
            _checking = false;
            Changed();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task InstallAsync(ReleaseInfo release)
    {
        if (_downloading) return;
        _downloading = true;
        _error = null;
        _progress = 0;
        Changed();
        try
        {
            var progress = new Progress<double>(p =>
            {
                if (Math.Round(p * 100) == Math.Round(_progress * 100)) return;
                _progress = p;
                Changed();
            });
            var setup = await _service.DownloadAsync(release, progress);
            UpdateService.StartInstaller(setup);
            // Close the app (App.OnExit saves profiles and settings); the setup replaces the exe and starts the new one.
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Warn("Update installeren mislukt", ex);
            _error = ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }
                ? "je hebt de installatie geannuleerd."
                : ex.Message;
            _downloading = false;
            Changed();
        }
    }

    /// <summary>Shows "this is new" once when this version runs for the first time after an update.</summary>
    private void DetectJustUpdated()
    {
        var current = UpdateService.CurrentVersion;
        var store = _host.SettingsStore;
        var last = store.Read(s => s.LastRunVersion);
        // No LastRunVersion but an existing settings file: updated from a version before this feature.
        bool updated = ReleaseVersion.TryParse(last, out var previous) ? current > previous : last == null && store.ExistedAtStartup;
        if (updated)
        {
            _justUpdated = (current, ReleaseNotes.NewestFromChangelog(Changelog.Read()));
            Log.Info($"Bijgewerkt van {last ?? "een oudere versie"} naar {current}.");
        }
        if (last != current.ToString()) store.Update(s => s.LastRunVersion = current.ToString());
    }

    private void Changed()
    {
        Version++;
    }

    private static string Bullets(IReadOnlyList<string> notes)
    {
        var lines = notes.Take(MaxNotesInBanner).Select(n => "• " + n);
        var text = string.Join("\n", lines);
        if (notes.Count > MaxNotesInBanner) text += $"\n• en nog {notes.Count - MaxNotesInBanner} andere verbeteringen";
        return text;
    }
}

/// <summary>CHANGELOG.md is built into the exe, so "what's new" also works offline after an update.</summary>
internal static class Changelog
{
    public static string Read()
    {
        try
        {
            using var stream = typeof(Changelog).Assembly.GetManifestResourceStream("CHANGELOG.md");
            if (stream == null) return "";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Log.Warn("CHANGELOG lezen mislukt", ex);
            return "";
        }
    }
}
