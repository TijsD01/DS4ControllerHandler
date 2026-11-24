using System.Windows.Input;
using DS4Handler.App.Infrastructure;
using DS4Handler.App.Lang;
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
    private static string ChangelogLink => Loc.Get("Update_ChangelogLink");
    private static readonly ICommand OpenChangelog = new RelayCommand(() => AppHost.OpenUrl(UpdateCheck.ChangelogUrl));

    private readonly AppHost _host;
    private readonly UpdateService _service = new();
    private DateTime _nextCheck = DateTime.UtcNow + FirstCheckDelay;
    private UpdateInfo? _available;
    private bool _availableDismissed;
    private bool _checking;
    private bool _downloading;
    private double _progress;
    private string? _error;
    private string _status = Loc.Get("Update_NotChecked");
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

    public string CurrentVersionText => Loc.Format("Update_Version", UpdateService.CurrentVersion);

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
                Title = Loc.Format("Update_UpdatedTitle", done.Version),
                Text = done.Notes.Count > 0
                    ? Loc.Get("Update_NewInThisVersion") + "\n" + Bullets(done.Notes) + "\n" + Loc.Get("Update_SettingsKept")
                    : Loc.Get("Update_SettingsKept"),
                LinkText = ChangelogLink,
                LinkCommand = OpenChangelog,
                PrimaryText = Loc.Get("Update_Close"),
                PrimaryCommand = new RelayCommand(() => { _justUpdated = null; Changed(); }),
                PrimaryTip = Loc.Get("Update_CloseTip"),
            };
        }

        if (_available is { } update && !_availableDismissed)
        {
            var notes = update.LatestNotes;
            // The notes themselves come from the release (Dutch); only the words around them are translated.
            string whatsNew = notes.Count > 0 ? "\n\n" + Loc.Format("Update_NewIn", update.Latest.Version) + "\n" + Bullets(notes) : "";
            if (_downloading)
            {
                yield return new NoticeViewModel
                {
                    Level = Level.Learning,
                    Title = Loc.Format("Update_Downloading", update.Latest.Version, Math.Round(_progress * 100)),
                    Text = Loc.Get("Update_DownloadingText"),
                };
            }
            else if (UpdateService.IsInstalled)
            {
                yield return new NoticeViewModel
                {
                    Level = Level.Learning,
                    Title = Loc.Format("Update_AvailableTitle", update.Latest.Version),
                    Text = (_error != null ? Loc.Format("Update_Failed", _error) + "\n\n" : "")
                           + Loc.Format("Update_InstalledText", UpdateService.CurrentVersion)
                           + whatsNew,
                    PrimaryText = _error != null ? Loc.Get("Update_TryAgain") : Loc.Get("Update_UpdateNow"),
                    PrimaryCommand = new RelayCommand(() => _ = InstallAsync(update.Latest)),
                    PrimaryTip = Tips.UpdateNow,
                    SecondaryText = Loc.Get("Update_Later"),
                    SecondaryCommand = new RelayCommand(() => { _availableDismissed = true; Changed(); }),
                    SecondaryTip = Tips.UpdateLater,
                    LinkText = ChangelogLink,
                    LinkCommand = OpenChangelog,
                };
            }
            else
            {
                // Portable exe: there is no installation to upgrade, so point to the download.
                yield return new NoticeViewModel
                {
                    Level = Level.Learning,
                    Title = Loc.Format("Update_AvailableTitle", update.Latest.Version),
                    Text = Loc.Format("Update_PortableText", UpdateService.CurrentVersion) + whatsNew,
                    PrimaryText = Loc.Get("Update_OpenDownloadPage"),
                    PrimaryCommand = new RelayCommand(() => AppHost.OpenUrl(string.IsNullOrEmpty(update.Latest.PageUrl) ? UpdateCheck.ReleasesPageUrl : update.Latest.PageUrl)),
                    PrimaryTip = Tips.UpdateDownloadPage,
                    SecondaryText = Loc.Get("Update_Later"),
                    SecondaryCommand = new RelayCommand(() => { _availableDismissed = true; Changed(); }),
                    SecondaryTip = Tips.UpdateLater,
                    LinkText = ChangelogLink,
                    LinkCommand = OpenChangelog,
                };
            }
        }
    }

    private async Task CheckAsync(bool manual)
    {
        if (_checking || _downloading) return;
        _checking = true;
        Status = Loc.Get("Update_Checking");
        try
        {
            var update = await _service.CheckAsync();
            _nextCheck = DateTime.UtcNow + CheckInterval;
            if (update != null)
            {
                bool isNew = _available?.Latest.Version != update.Latest.Version;
                _available = update;
                if (isNew || manual) { _availableDismissed = false; _error = null; }
                Status = Loc.Format("Update_StatusAvailable", update.Latest.Version, UpdateService.CurrentVersion);
            }
            else
            {
                Status = Loc.Format("Update_StatusUpToDate", DateTime.Now.ToString("HH:mm", Loc.Culture));
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
                ? Loc.Get("Update_Cancelled")
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
        // Running on defaults (unreadable or damaged settings.json): LastRunVersion is unknown, so "updated" cannot be told.
        if (store.ReadOnly) return;
        var last = store.Read(s => s.LastRunVersion);
        // No LastRunVersion but an existing settings file: updated from a version before this feature.
        bool updated = ReleaseVersion.TryParse(last, out var previous) ? current > previous
            : last == null && store.ExistedAtStartup && !store.Salvaged;
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
        if (notes.Count > MaxNotesInBanner) text += "\n• " + Loc.Format("Update_MoreNotes", notes.Count - MaxNotesInBanner);
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
