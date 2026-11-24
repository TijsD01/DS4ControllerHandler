using System.Collections.ObjectModel;
using System.Windows.Input;
using DS4Handler.App.Engine;
using DS4Handler.App.Infrastructure;
using DS4Handler.App.Lang;
using DS4Handler.App.Services;

namespace DS4Handler.App.ViewModels;

/// <summary>A banner at the top of the window (missing driver, HidHide busy, ...).</summary>
public sealed class NoticeViewModel
{
    public required string Level { get; init; }
    public required string Title { get; init; }
    public required string Text { get; init; }
    public string? PrimaryText { get; init; }
    public ICommand? PrimaryCommand { get; init; }
    public string? PrimaryTip { get; init; }
    public string? SecondaryText { get; init; }
    public ICommand? SecondaryCommand { get; init; }
    public string? SecondaryTip { get; init; }
    /// <summary>Optional text link under the body, e.g. to the full changelog.</summary>
    public string? LinkText { get; init; }
    public ICommand? LinkCommand { get; init; }
    public bool HasLink => LinkText != null;
    public bool HasPrimary => PrimaryText != null;
    public bool HasSecondary => SecondaryText != null;
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly Func<string, string, string, bool> _confirm;
    private int _managerVersion = -1;
    private int _hidHideVersion = -1;
    private string _noticeKey = "";
    private string _page = "controllers";
    private string _driverChip = Loc.Get("Common_Checking");  // short driver texts for DriversText
    private string _driverLevel = Level.Learning;
    private string _driverTip = Tips.DriverChecking;
    private string _hidHideChip = Loc.Get("Common_Checking");
    private string _hidHideLevel = Level.Learning;
    private string _hidHideTip = "";
    private ControllerViewModel? _selected;
    private int _slowTick;

    public MainViewModel(AppHost host, IDialogs dialogs)
    {
        _host = host;
        _confirm = dialogs.Confirm;
        Settings = new SettingsViewModel(host);
        ProfilesPage = new ProfilesViewModel(host, dialogs);
        ShowControllersCommand = new RelayCommand(() => Page = PageControllers);
        ShowSettingsCommand = new RelayCommand(() => Page = PageSettings);
        SelectControllerCommand = new RelayCommand<ControllerViewModel>(c =>
        {
            if (c == null) return;
            SelectedController = c;
            Page = PageControllers;
        });
    }

    private const string PageControllers = "controllers", PageProfiles = "profiles", PageSettings = "settings";

    public ObservableCollection<ControllerViewModel> Controllers { get; } = new();
    public ObservableCollection<NoticeViewModel> Notices { get; } = new();
    public SettingsViewModel Settings { get; }
    public ProfilesViewModel ProfilesPage { get; }
    public ICommand ShowControllersCommand { get; }
    public ICommand ShowSettingsCommand { get; }

    /// <summary>Top bar: show this controller's page (parameter: the <see cref="ControllerViewModel"/>).</summary>
    public ICommand SelectControllerCommand { get; }

    /// <summary>The controller whose page is shown on the controller page. Follows connects and disconnects.</summary>
    public ControllerViewModel? SelectedController
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            UpdateShown();
        }
    }

    /// <summary>Version number next to the wordmark in the top bar.</summary>
    public string VersionText => UpdateService.CurrentVersion.ToString();

    public bool HasControllers => Controllers.Count > 0;
    public bool IsEmpty => Controllers.Count == 0;

    private string Page
    {
        get => _page;
        set
        {
            if (!SetField(ref _page, value)) return;
            OnPropertyChanged(nameof(ShowControllers));
            OnPropertyChanged(nameof(ShowProfiles));
            OnPropertyChanged(nameof(ShowSettings));
            UpdateShown();
            if (value == PageProfiles) ProfilesPage.Refresh();
        }
    }

    // Each tab's RadioButton binds to one of these; unchecking (false) is ignored, checking switches the page.
    public bool ShowControllers { get => _page == PageControllers; set { if (value) Page = PageControllers; } }
    public bool ShowProfiles { get => _page == PageProfiles; set { if (value) Page = PageProfiles; } }
    public bool ShowSettings { get => _page == PageSettings; set { if (value) Page = PageSettings; } }

    public string DriverLevel { get => _driverLevel; private set => SetField(ref _driverLevel, value); }
    public string DriverTip { get => _driverTip; private set => SetField(ref _driverTip, value); }
    public string HidHideLevel { get => _hidHideLevel; private set => SetField(ref _hidHideLevel, value); }
    public string HidHideTip { get => _hidHideTip; private set => SetField(ref _hidHideTip, value); }


    /// <summary>Compact driver status in the top bar: one dot and a short text for ViGEmBus and HidHide together.</summary>
    public string DriversText { get => _driversText; private set => SetField(ref _driversText, value); }
    public string DriversLevel { get => _driversLevel; private set => SetField(ref _driversLevel, value); }
    private string _driversText = Loc.Get("Common_Checking");
    private string _driversLevel = Level.Learning;

    /// <summary>Short text for the tray icon.</summary>
    public string TrayText => Controllers.Count switch
    {
        0 => Loc.Get("Tray_TextNone"),
        1 => Loc.Get("Tray_TextOne"),
        var n => Loc.Format("Tray_TextMany", n),
    };

    /// <summary>Learning state for the tray menu: true when every connected controller is learning.</summary>
    public bool AllLearning => Controllers.Count == 0 || Controllers.All(c => c.LearningEnabled);

    /// <summary>~30 Hz while the window is visible (live values); also called at 1 Hz from the tray.</summary>
    public void Tick(bool visible)
    {
        SyncControllers();
        if (visible)
        {
            foreach (var c in Controllers) c.Tick();
        }
        if (!visible || _slowTick++ % 15 == 0)
        {
            Settings.Updates.Tick();
            UpdateStatus();
        }
    }

    public void SetLearningAll(bool enabled)
    {
        _host.SetLearningAll(enabled);
        foreach (var c in Controllers) c.RefreshLearning();
    }

    private void SyncControllers()
    {
        int version = _host.Manager.Version;
        if (version == _managerVersion) return;
        _managerVersion = version;

        var sessions = _host.Manager.Sessions;
        for (int i = Controllers.Count - 1; i >= 0; i--)
            if (!sessions.Contains(Controllers[i].Session))
            {
                _host.Library.MarkSeen(new[] { Controllers[i].Session }); // "Laatst gezien" on the Profielen tab
                Controllers.RemoveAt(i);
            }
        foreach (var s in sessions)
            if (!Controllers.Any(c => c.Session == s)) Controllers.Add(new ControllerViewModel(s, _host, _confirm));

        // Keep a controller selected: the first one that connects, or another one when the selected one leaves.
        if (_selected == null || !Controllers.Contains(_selected))
            SelectedController = Controllers.FirstOrDefault();

        OnPropertyChanged(nameof(HasControllers));
        OnPropertyChanged(nameof(IsEmpty));
        if (ShowProfiles) ProfilesPage.Refresh();
        OnPropertyChanged(nameof(TrayText));
        _noticeKey = ""; // blocked count may have changed
    }

    private void UpdateShown()
    {
        foreach (var c in Controllers) c.IsShown = c == _selected && _page == PageControllers;
    }

    private void UpdateStatus()
    {
        var manager = _host.Manager;
        var hidHide = _host.HidHide;
        bool hidHideEnabled = _host.SettingsStore.Read(s => s.HidHideEnabled);

        switch (manager.DriverState)
        {
            case VirtualDriverState.Ready:
                _driverChip = Loc.Get("Main_DriverOk"); DriverLevel = Level.Ok; DriverTip = Tips.DriverReady; break;
            case VirtualDriverState.Missing:
                _driverChip = Loc.Get("Main_DriverMissing"); DriverLevel = Level.Bad; DriverTip = Tips.DriverMissing; break;
            case VirtualDriverState.Failed:
                _driverChip = Loc.Get("Main_DriverError"); DriverLevel = Level.Bad; DriverTip = Loc.Format("Main_DriverErrorTip", manager.DriverError); break;
            default:
                _driverChip = Loc.Get("Common_Checking"); DriverLevel = Level.Learning; DriverTip = Tips.DriverChecking; break;
        }

        if (!hidHideEnabled)
        {
            _hidHideChip = Loc.Get("Main_HidHideOff"); HidHideLevel = Level.Off;
            HidHideTip = Loc.Get("Main_HidHideOffTip");
        }
        else
        {
            switch (hidHide.Availability)
            {
                case HidHideAvailability.Ready when hidHide.Problem == null:
                    _hidHideChip = Loc.Get("Main_HidHideActive"); HidHideLevel = Level.Ok;
                    HidHideTip = Loc.Get("Main_HidHideActiveTip");
                    break;
                case HidHideAvailability.Ready:
                    _hidHideChip = Loc.Get("Main_HidHideAttention"); HidHideLevel = Level.Warn; HidHideTip = hidHide.Problem!; break;
                case HidHideAvailability.NotInstalled:
                    _hidHideChip = Loc.Get("Main_HidHideMissing"); HidHideLevel = Level.Warn;
                    HidHideTip = Loc.Get("Main_HidHideMissingTip"); break;
                case HidHideAvailability.Busy:
                    _hidHideChip = Loc.Get("Main_HidHideBusy"); HidHideLevel = Level.Warn;
                    HidHideTip = Loc.Get("Main_HidHideBusyTip"); break;
                case HidHideAvailability.Error:
                    _hidHideChip = Loc.Get("Main_HidHideError"); HidHideLevel = Level.Warn;
                    HidHideTip = Loc.Get("Main_HidHideErrorTip"); break;
                default:
                    _hidHideChip = Loc.Get("Common_Checking"); HidHideLevel = Level.Learning; HidHideTip = Loc.Get("Main_HidHideCheckingTip"); break;
            }
        }

        // One line for the top bar: the most important problem, otherwise "in orde".
        if (DriverLevel is Level.Bad or Level.Warn) { DriversText = _driverChip; DriversLevel = DriverLevel; }
        else if (HidHideLevel is Level.Bad or Level.Warn) { DriversText = _hidHideChip; DriversLevel = HidHideLevel; }
        else if (DriverLevel == Level.Learning || HidHideLevel == Level.Learning) { DriversText = Loc.Get("Common_Checking"); DriversLevel = Level.Learning; }
        else { DriversText = Loc.Get("Main_DriversOk"); DriversLevel = Level.Ok; }

        Settings.Refresh();
        UpdateNotices(manager, hidHide, hidHideEnabled);
    }

    private void UpdateNotices(ControllerManager manager, HidHideService hidHide, bool hidHideEnabled)
    {
        int blocked = manager.BlockedCount;
        string key = $"{manager.DriverState}|{hidHide.Availability}|{hidHide.Problem}|{hidHideEnabled}|{blocked}|{Settings.Updates.Version}";
        if (key == _noticeKey && hidHide.Version == _hidHideVersion) return;
        _noticeKey = key;
        _hidHideVersion = hidHide.Version;

        var recheck = new RelayCommand(_host.RecheckHidHide);
        var list = new List<NoticeViewModel>();

        if (manager.DriverState == VirtualDriverState.Missing)
        {
            list.Add(new NoticeViewModel
            {
                Level = Level.Bad,
                Title = Loc.Get("Notice_ViGEmMissingTitle"),
                Text = Loc.Get("Notice_ViGEmMissingText"),
                PrimaryText = Loc.Get("Common_DownloadViGEm"),
                PrimaryCommand = new RelayCommand(() => AppHost.OpenUrl(AppHost.ViGEmDownloadUrl)),
                PrimaryTip = Tips.DownloadViGEm,
            });
        }
        else if (manager.DriverState == VirtualDriverState.Failed)
        {
            list.Add(new NoticeViewModel
            {
                Level = Level.Bad,
                Title = Loc.Get("Notice_ViGEmFailedTitle"),
                Text = Loc.Format("Notice_ViGEmFailedText", manager.DriverError),
                PrimaryText = Loc.Get("Common_DownloadViGEm"),
                PrimaryCommand = new RelayCommand(() => AppHost.OpenUrl(AppHost.ViGEmDownloadUrl)),
                PrimaryTip = Tips.DownloadViGEm,
            });
        }

        if (hidHideEnabled)
        {
            switch (hidHide.Availability)
            {
                case HidHideAvailability.NotInstalled:
                    list.Add(new NoticeViewModel
                    {
                        Level = Level.Warn,
                        Title = Loc.Get("Notice_HidHideMissingTitle"),
                        Text = Loc.Get("Notice_HidHideMissingText"),
                        PrimaryText = Loc.Get("Common_DownloadHidHide"),
                        PrimaryCommand = new RelayCommand(() => AppHost.OpenUrl(HidHideService.DownloadUrl)),
                        PrimaryTip = Tips.DownloadHidHide,
                        SecondaryText = Loc.Get("Common_Recheck"),
                        SecondaryCommand = recheck,
                        SecondaryTip = Tips.RecheckHidHide,
                    });
                    break;
                case HidHideAvailability.Busy:
                    list.Add(new NoticeViewModel
                    {
                        Level = Level.Warn,
                        Title = Loc.Get("Notice_HidHideBusyTitle"),
                        Text = Loc.Get("Notice_HidHideBusyText"),
                        PrimaryText = Loc.Get("Common_Recheck"),
                        PrimaryCommand = recheck,
                        PrimaryTip = Tips.RecheckHidHide,
                    });
                    break;
                case HidHideAvailability.Error:
                    list.Add(new NoticeViewModel
                    {
                        Level = Level.Warn,
                        Title = Loc.Get("Notice_HidHideErrorTitle"),
                        Text = Loc.Get("Notice_HidHideErrorText"),
                        PrimaryText = Loc.Get("Common_Recheck"),
                        PrimaryCommand = recheck,
                        PrimaryTip = Tips.RecheckHidHide,
                    });
                    break;
            }
            if (hidHide.Problem is { } problem && hidHide.Availability == HidHideAvailability.Ready)
            {
                list.Add(new NoticeViewModel
                {
                    Level = Level.Warn,
                    Title = Loc.Get("Notice_HideFailedTitle"),
                    Text = problem,
                    PrimaryText = Loc.Get("Notice_TryAgain"),
                    PrimaryCommand = recheck,
                    PrimaryTip = Tips.RecheckHidHide,
                });
            }
        }

        if (blocked > 0)
        {
            list.Add(new NoticeViewModel
            {
                Level = Level.Warn,
                Title = blocked == 1 ? Loc.Get("Notice_BlockedOne") : Loc.Format("Notice_BlockedMany", blocked),
                Text = Loc.Get("Notice_BlockedText"),
                PrimaryText = Loc.Get("Common_OpenLog"),
                PrimaryCommand = new RelayCommand(() => AppHost.OpenUrl(Log.FilePath)),
                PrimaryTip = Tips.OpenLog,
            });
        }

        list.AddRange(Settings.Updates.Notices());

        Notices.Clear();
        foreach (var n in list) Notices.Add(n);
    }
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppHost _host;
    private string _hidHideStatus = "";
    private string _hidHideLevel = Level.Learning;

    public SettingsViewModel(AppHost host)
    {
        _host = host;
        RecheckHidHideCommand = new RelayCommand(host.RecheckHidHide);
        DownloadHidHideCommand = new RelayCommand(() => AppHost.OpenUrl(HidHideService.DownloadUrl));
        DownloadViGEmCommand = new RelayCommand(() => AppHost.OpenUrl(AppHost.ViGEmDownloadUrl));
        OpenProfilesCommand = new RelayCommand(() => AppHost.OpenFolder(host.Profiles.Directory));
        OpenLogCommand = new RelayCommand(() => AppHost.OpenUrl(Log.FilePath));
        QuitCommand = new RelayCommand(() => QuitRequested?.Invoke(this, EventArgs.Empty));
        RestartCommand = new RelayCommand(() => RestartRequested?.Invoke(this, EventArgs.Empty));
        Updates = new UpdateViewModel(host);
    }

    /// <summary>"Afsluiten": quits the app completely (the main window forwards this to the app).</summary>
    public ICommand QuitCommand { get; }

    /// <summary>Raised by <see cref="QuitCommand"/>.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>"Nu herstarten" after a language change: quits and starts the app again.</summary>
    public ICommand RestartCommand { get; }

    /// <summary>Raised by <see cref="RestartCommand"/>.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>The language menu: settings value (Key) and name in its own language (Value).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Languages { get; } =
        Loc.Languages.Select(l => KeyValuePair.Create(l.Code, l.Name)).ToList();

    // The UI text is read once at startup (Loc.Apply), so a new choice only shows after a restart.
    private readonly string _languageInUse = Loc.Culture.TwoLetterISOLanguageName;

    /// <summary>The saved language, or the one in use when none is saved.</summary>
    public string Language
    {
        get => _host.SettingsStore.Read(s => s.Language) is { } code && Loc.Languages.Any(l => l.Code == code) ? code : _languageInUse;
        set
        {
            if (value == Language) return;
            _host.SettingsStore.Update(s => s.Language = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RestartNeeded));
        }
    }

    public bool RestartNeeded => Language != _languageInUse;

    public ICommand RecheckHidHideCommand { get; }
    public ICommand DownloadHidHideCommand { get; }
    public ICommand DownloadViGEmCommand { get; }
    public ICommand OpenProfilesCommand { get; }
    public ICommand OpenLogCommand { get; }

    /// <summary>The Updates block on this page; also feeds the update banner of the main window.</summary>
    public UpdateViewModel Updates { get; }

    /// <summary>First line of the Over card: "DS4 Handler 2.0.0".</summary>
    public string Version => "DS4 Handler " + UpdateService.CurrentVersion;

    public bool StartWithWindows
    {
        get => _host.SettingsStore.Read(s => s.StartWithWindows);
        set { _host.SetStartWithWindows(value); OnPropertyChanged(); }
    }

    public bool StartMinimized
    {
        get => _host.SettingsStore.Read(s => s.StartMinimized);
        set { _host.SettingsStore.Update(s => s.StartMinimized = value); OnPropertyChanged(); }
    }

    public bool HidHideEnabled
    {
        get => _host.SettingsStore.Read(s => s.HidHideEnabled);
        set { _host.SetHidHideEnabled(value); OnPropertyChanged(); }
    }

    public bool UnhideOnExit
    {
        get => _host.SettingsStore.Read(s => s.UnhideOnExit);
        set { _host.SettingsStore.Update(s => s.UnhideOnExit = value); OnPropertyChanged(); }
    }

    public string HidHideStatus { get => _hidHideStatus; private set => SetField(ref _hidHideStatus, value); }
    public string HidHideLevel { get => _hidHideLevel; private set => SetField(ref _hidHideLevel, value); }


    internal void Refresh()
    {
        var h = _host.HidHide;
        (HidHideStatus, HidHideLevel) = h.Availability switch
        {
            HidHideAvailability.Ready when h.Problem != null => (h.Problem, Level.Warn),
            HidHideAvailability.Ready => (Loc.Get("Settings_HidHideReady"), Level.Ok),
            HidHideAvailability.NotInstalled => (Loc.Get("Settings_HidHideMissing"), Level.Warn),
            HidHideAvailability.Busy => (Loc.Get("Settings_HidHideBusy"), Level.Warn),
            HidHideAvailability.Error => (Loc.Get("Settings_HidHideError"), Level.Bad),
            _ => (Loc.Get("Settings_HidHideChecking"), Level.Learning),
        };
    }
}
