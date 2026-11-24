using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DS4Handler.App.Engine;
using DS4Handler.App.Infrastructure;
using DS4Handler.App.Lang;
using DS4Handler.App.Services;
using DS4Handler.Core.Input;
using DS4Handler.Core.Profiles;

namespace DS4Handler.App.ViewModels;

/// <summary>Dialogs the view models may show (implemented by the views).</summary>
public interface IDialogs
{
    bool Confirm(string title, string message, string confirmText);
    void Inform(string title, string message);
    /// <summary>Returns the chosen path, or null when cancelled.</summary>
    string? SaveProfileFile(string suggestedName);
    string? OpenProfileFile();
}

/// <summary>A target in the "Kopiëren naar…" menu or the "Profiel kiezen" list.</summary>
public sealed record ProfileChoice(string Id, string Label, string Tip);

/// <summary>One row in the profile list.</summary>
public sealed class ProfileRowViewModel : ObservableObject
{
    private readonly ProfilesViewModel _owner;
    private string _name;

    internal ProfileRowViewModel(ProfilesViewModel owner, ProfileInfo info, string displayName, IReadOnlyList<ProfileChoice> copyTargets)
    {
        _owner = owner;
        Info = info;
        _name = info.Name ?? "";
        DisplayName = displayName;
        CopyTargets = copyTargets;
        RenameCommand = new RelayCommand(() => RenameRequested?.Invoke(this, EventArgs.Empty));
        ExportCommand = new RelayCommand(() => _owner.Export(this));
        DeleteCommand = new RelayCommand(() => _owner.Delete(this), () => CanDelete);
    }

    internal ProfileInfo Info { get; }
    public string Id => Info.Id;

    /// <summary>Raised when "Hernoemen" is clicked, so the view can focus the name box.</summary>
    public event EventHandler? RenameRequested;

    /// <summary>Editable friendly name; empty means the model name is shown.</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? "");
    }

    public string DisplayName { get; }
    public string NamePlaceholder => ModelText;

    public string MacText => Info.Mac ?? Loc.Get("Profile_NoMacFound");
    public string MacTip => Info.Mac != null ? Tips.ProfileMac : Tips.ProfileNoMac;

    public string ModelText => Info.Model switch
    {
        ControllerModel.DualShock4 => "DualShock 4",
        ControllerModel.DualSense => "DualSense",
        _ => Loc.Get("Profile_UnknownModel"),
    };

    public bool IsConnected => Info.Connected != null;
    public string StatusLevel => IsConnected ? Level.Ok : Level.Off;

    public string StatusText => Info.Connected is { } s
        ? Loc.Format("Profile_ConnectedVia", s.Connection == ConnectionType.Bluetooth ? "Bluetooth" : "USB")
        : Info.LastSeenUtc is { } seen
            ? Loc.Format("Profile_LastSeen", seen.ToLocalTime().ToString("d MMMM yyyy, HH:mm", Loc.Culture))
            : Loc.Get("Profile_NeverConnected");

    public string StatusTip => IsConnected ? Tips.ProfileConnected : Tips.ProfileLastSeen;

    public string Summary => ProfilesViewModel.Summarize(Info.Profile);

    public string UsedByText
    {
        get
        {
            var others = Info.UsedBy.Where(s => !string.Equals(s.Id, Info.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (others.Length == 0) return "";
            return Loc.Format("Profile_AlsoUsedBy", string.Join(", ", others.Select(s => _owner.NameOf(s.Id))));
        }
    }

    public bool CanDelete => Info.Connected == null && Info.UsedBy.Count == 0;
    public string DeleteTip => CanDelete ? Tips.ProfileDelete : Tips.ProfileDeleteBlocked;

    public IReadOnlyList<ProfileChoice> CopyTargets { get; }
    public bool CanCopy => CopyTargets.Count > 0 && Info.Profile != null;
    public string CopyTip => CanCopy ? Tips.ProfileCopy : Tips.ProfileCopyNone;

    public ICommand RenameCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand DeleteCommand { get; }

    /// <summary>Undoes an edit that was not saved yet.</summary>
    public void RevertName() => Name = Info.Name ?? "";

    /// <summary>Saves the edited name (called by the view when the name box loses focus or Enter is pressed).</summary>
    public void CommitName() => _owner.Rename(this, Name);

    /// <summary>Copies this profile's calibration onto <paramref name="target"/> (after confirmation).</summary>
    public void CopyTo(ProfileChoice target) => _owner.Copy(this, target);
}

/// <summary>"Profiel kiezen" for one connected controller.</summary>
public sealed class LinkRowViewModel : ObservableObject
{
    private readonly ProfilesViewModel _owner;
    private ProfileChoice? _selected;
    private bool _syncing;

    internal LinkRowViewModel(ProfilesViewModel owner, ControllerSession session, string name, IReadOnlyList<ProfileChoice> choices)
    {
        _owner = owner;
        Session = session;
        Name = name;
        Choices = choices;
        _syncing = true;
        Selected = choices.FirstOrDefault(c => string.Equals(c.Id, session.ProfileId, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
        _syncing = false;
    }

    public ControllerSession Session { get; }
    public string Name { get; }
    public string MacText => Session.Mac ?? Loc.Get("Profile_NoMacFound");
    public IReadOnlyList<ProfileChoice> Choices { get; }

    public ProfileChoice? Selected
    {
        get => _selected;
        set
        {
            var previous = _selected;
            if (!SetField(ref _selected, value) || _syncing || value == null) return;
            // Linking does file I/O: it runs on a worker; a refusal puts the previous choice back.
            _owner.LinkAsync(this, value, () =>
            {
                _syncing = true;
                Selected = previous;
                _syncing = false;
            });
        }
    }

    public string StatusText => Session.ProfileBlocked
        ? Loc.Get("Profile_LinkBlocked")
        : string.Equals(Session.ProfileId, Session.Id, StringComparison.OrdinalIgnoreCase)
            ? Loc.Get("Profile_LinkOwn")
            : Loc.Format("Profile_LinkShared", _owner.NameOf(Session.ProfileId));

    public string StatusLevel => Session.ProfileBlocked ? Level.Warn : Level.Ok;
}

/// <summary>The "Profielen" tab.</summary>
public sealed class ProfilesViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly IDialogs _dialogs;
    private bool _busy;
    private int _refreshVersion;

    public ProfilesViewModel(AppHost host, IDialogs dialogs)
    {
        _host = host;
        _dialogs = dialogs;
        ImportCommand = new RelayCommand(Import, () => !Busy);
        OpenFolderCommand = new RelayCommand(() => AppHost.OpenFolder(host.Profiles.Directory));
    }

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = new();
    public ObservableCollection<LinkRowViewModel> Connected { get; } = new();
    public ICommand ImportCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public bool HasProfiles => Profiles.Count > 0;
    public bool HasConnected => Connected.Count > 0;

    public bool Busy
    {
        get => _busy;
        private set => SetField(ref _busy, value);
    }

    internal string NameOf(string id) => _host.Library.DisplayName(id);

    /// <summary>Reloads the list in the background (file I/O) and fills it on the UI thread.</summary>
    public void Refresh()
    {
        int version = ++_refreshVersion;
        var dispatcher = Application.Current?.Dispatcher;
        Task.Run(() =>
        {
            try { return _host.Library.Load(); }
            catch (Exception ex) { Log.Warn("Profielen laden mislukt", ex); return Array.Empty<ProfileInfo>(); }
        }).ContinueWith(t => dispatcher?.BeginInvoke(() =>
        {
            if (version == _refreshVersion) Apply(t.Result); // a newer refresh wins
        }), TaskScheduler.Default);
    }

    private void Apply(IReadOnlyList<ProfileInfo> infos)
    {
        Profiles.Clear();
        foreach (var info in infos)
        {
            var targets = infos
                .Where(o => !string.Equals(o.Id, info.Id, StringComparison.OrdinalIgnoreCase))
                .Select(o => new ProfileChoice(o.Id, Label(o), Tips.ProfileCopy))
                .ToArray();
            Profiles.Add(new ProfileRowViewModel(this, info, NameOf(info.Id), targets));
        }

        Connected.Clear();
        foreach (var session in _host.Manager.Sessions)
        {
            var choices = new List<ProfileChoice> { new(session.Id, Loc.Format("Profile_ChoiceOwn", NameOf(session.Id)), Tips.LinkOwn) };
            foreach (var info in infos)
            {
                if (string.Equals(info.Id, session.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (info.Model != null && info.Model != session.Model) continue; // only the same kind of controller
                bool inUseByOther = info.UsedBy.Any(s => s != session) || (info.Connected != null && info.Connected != session);
                if (inUseByOther && !string.Equals(info.Id, session.ProfileId, StringComparison.OrdinalIgnoreCase)) continue;
                choices.Add(new ProfileChoice(info.Id, Loc.Format("Profile_ChoiceShared", Label(info)), Tips.LinkShared));
            }
            Connected.Add(new LinkRowViewModel(this, session, NameOf(session.Id), choices));
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasConnected));
    }

    private string Label(ProfileInfo info) => NameOf(info.Id) + "  ·  " + (info.Mac ?? Loc.Get("Profile_NoMac"));

    internal void Rename(ProfileRowViewModel row, string name)
    {
        if ((row.Info.Name ?? "") == name.Trim()) return;
        RunAsync(() => { _host.Library.Rename(row.Id, name); return null; }, refresh: true);
    }

    internal void LinkAsync(LinkRowViewModel row, ProfileChoice choice, Action revert) =>
        RunAsync(() => _host.Library.Link(row.Session, choice.Id), refresh: true, onError: revert, errorTitle: Loc.Get("Profile_ChooseErrorTitle"));

    internal void Copy(ProfileRowViewModel row, ProfileChoice target)
    {
        if (!_dialogs.Confirm(
                Loc.Get("Profile_CopyTitle"),
                Loc.Format("Profile_CopyMessage", row.DisplayName, NameOf(target.Id)),
                Loc.Get("Profile_CopyConfirm")))
            return;
        RunAsync(() => _host.Library.Copy(row.Id, target.Id), refresh: true, done: Loc.Get("Profile_CopyDone"));
    }

    internal void Export(ProfileRowViewModel row)
    {
        var path = _dialogs.SaveProfileFile(SafeFileName(row.DisplayName) + ".ds4profile.json");
        if (path == null) return;
        RunAsync(() => _host.Library.Export(row.Id, path), refresh: false,
            done: Loc.Get("Profile_ExportDone"));
    }

    internal void Delete(ProfileRowViewModel row)
    {
        if (!row.CanDelete) return;
        if (!_dialogs.Confirm(
                Loc.Get("Profile_DeleteTitle"),
                Loc.Format("Profile_DeleteMessage", row.DisplayName, row.MacText),
                Loc.Get("Profile_DeleteConfirm")))
            return;
        RunAsync(() => _host.Library.Delete(row.Id), refresh: true);
    }

    /// <summary>Largest profile file accepted; real ones are a few kB.</summary>
    private const long MaxImportBytes = 1024 * 1024;

    private void Import()
    {
        var path = _dialogs.OpenProfileFile();
        if (path == null) return;

        try
        {
            if (new FileInfo(path).Length > MaxImportBytes)
            {
                _dialogs.Inform(Loc.Get("Profile_ImportFailed"), Loc.Get("Profile_ImportTooLarge"));
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Inform(Loc.Get("Profile_ImportFailed"), Loc.Get("Profile_ImportUnreadable"));
            return;
        }

        // Reading and checking happen on a worker; the questions are asked back on the UI thread.
        Busy = true;
        var dispatcher = Application.Current?.Dispatcher;
        Task.Run(() =>
        {
            try
            {
                var imported = ProfileLibrary.Peek(path);
                return (Imported: imported, Exists: _host.Library.Exists(imported.Profile.ControllerId), Error: (string?)null);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Profiel importeren uit {path} mislukt", ex);
                return (Imported: (ImportedProfile?)null, Exists: false,
                    Error: (string?)Loc.Get("Profile_ImportInvalid"));
            }
        }).ContinueWith(t => dispatcher?.BeginInvoke(() =>
        {
            Busy = false;
            CommandManager.InvalidateRequerySuggested();
            var (imported, exists, error) = t.Result;
            if (imported == null)
            {
                _dialogs.Inform(Loc.Get("Profile_ImportFailed"), error ?? Loc.Get("Profile_UnknownError"));
                return;
            }

            var id = imported.Profile.ControllerId;
            string who = imported.Name ?? NameOf(id);
            string mac = ControllerIds.MacFromId(id) ?? Loc.Get("Profile_NoMac");
            if (exists &&
                !_dialogs.Confirm(Loc.Get("Profile_OverwriteTitle"),
                    Loc.Format("Profile_OverwriteMessage", mac, who),
                    Loc.Get("Profile_OverwriteConfirm")))
                return;

            RunAsync(() => _host.Library.Import(imported), refresh: true,
                done: Loc.Format("Profile_ImportDone", who, mac));
        }), TaskScheduler.Default);
    }

    /// <summary>Runs a profile action off the UI thread and reports its error text (if any).</summary>
    private void RunAsync(Func<string?> action, bool refresh, string? done = null, Action? onError = null, string? errorTitle = null)
    {
        Busy = true;
        var dispatcher = Application.Current?.Dispatcher;
        Task.Run(() =>
        {
            try { return action(); }
            catch (Exception ex) { Log.Error("Profielactie mislukt", ex); return Loc.Format("Profile_SomethingWentWrong", ex.Message); }
        }).ContinueWith(t => dispatcher?.BeginInvoke(() =>
        {
            Busy = false;
            CommandManager.InvalidateRequerySuggested();
            if (t.Result is { } error)
            {
                onError?.Invoke();
                _dialogs.Inform(errorTitle ?? Loc.Get("Profile_ErrorTitle"), error);
            }
            else if (done != null) _dialogs.Inform(Loc.Get("Profile_DoneTitle"), done);
            if (refresh) Refresh();
        }), TaskScheduler.Default);
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? Loc.Get("Profile_FileNameFallback") : clean;
    }

    /// <summary>Short plain-language summary of what a saved profile learned.</summary>
    internal static string Summarize(CalibrationProfile? p)
    {
        if (p == null) return Loc.Get("Profile_SumNothing");
        var parts = new List<string>(4);
        parts.Add(TriggerText("L2", p.L2));
        parts.Add(TriggerText("R2", p.R2));
        parts.Add(StickText(Loc.Get("Profile_SumLeftStick"), p.LeftStick));
        parts.Add(StickText(Loc.Get("Profile_SumRightStick"), p.RightStick));
        return string.Join(" · ", parts);

        static string TriggerText(string name, Core.Calibration.TriggerCalibrationData t)
        {
            if (!t.EndStopLearned) return Loc.Format("Profile_SumNotLearned", name);
            return t.EndStop >= 250 ? Loc.Format("Profile_SumFullRange", name) : Loc.Format("Profile_SumReaches", name, Fmt.Pct(t.EndStop / 255.0));
        }

        static string StickText(string name, Core.Calibration.StickCalibrationData s)
        {
            double drift = Math.Sqrt(s.CenterX * s.CenterX + s.CenterY * s.CenterY);
            if (s.Releases == 0) return Loc.Format("Profile_SumNotLearned", name);
            return drift >= 0.02 ? Loc.Format("Profile_SumDrift", name, Fmt.Pct(drift)) : Loc.Format("Profile_SumNoDrift", name);
        }
    }
}
