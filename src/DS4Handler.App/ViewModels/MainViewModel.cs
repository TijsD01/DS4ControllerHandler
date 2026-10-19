using System.Collections.ObjectModel;
using System.Windows.Input;
using DS4Handler.App.Engine;
using DS4Handler.App.Infrastructure;
using DS4Handler.App.Services;

namespace DS4Handler.App.ViewModels;

/// <summary>A banner at the top of the window (missing driver, HidHide busy, ...).</summary>
public sealed class NoticeViewModel
{
    public required string Level { get; init; }
    public required string Title { get; init; }
    public required string Text { get; init; }
    public string Icon => Level == ViewModels.Level.Bad ? "" : "";
    public string? PrimaryText { get; init; }
    public ICommand? PrimaryCommand { get; init; }
    public string? PrimaryTip { get; init; }
    public string? SecondaryText { get; init; }
    public ICommand? SecondaryCommand { get; init; }
    public string? SecondaryTip { get; init; }
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
    private string _driverChip = "Controleren…";
    private string _driverLevel = Level.Learning;
    private string _driverTip = Tips.DriverChecking;
    private string _hidHideChip = "Controleren…";
    private string _hidHideLevel = Level.Learning;
    private string _hidHideTip = "";
    private string _driverWord = "controleren…";
    private string _hidHideWord = "controleren…";
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

    /// <summary>Quits the app (the "Afsluiten" button on Instellingen; same as <see cref="SettingsViewModel.QuitCommand"/>).</summary>
    public ICommand QuitCommand => Settings.QuitCommand;

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

    public string DriverChip { get => _driverChip; private set => SetField(ref _driverChip, value); }
    public string DriverLevel { get => _driverLevel; private set => SetField(ref _driverLevel, value); }
    public string DriverTip { get => _driverTip; private set => SetField(ref _driverTip, value); }
    public string HidHideChip { get => _hidHideChip; private set => SetField(ref _hidHideChip, value); }
    public string HidHideLevel { get => _hidHideLevel; private set => SetField(ref _hidHideLevel, value); }
    public string HidHideTip { get => _hidHideTip; private set => SetField(ref _hidHideTip, value); }

    /// <summary>One- or two-word driver states for the driver tooltip ("werkt", "ontbreekt", ...).</summary>
    public string DriverWord { get => _driverWord; private set => SetField(ref _driverWord, value); }
    public string HidHideWord { get => _hidHideWord; private set => SetField(ref _hidHideWord, value); }

    /// <summary>Compact driver status in the top bar: one dot and a short text for ViGEmBus and HidHide together.</summary>
    public string DriversText { get => _driversText; private set => SetField(ref _driversText, value); }
    public string DriversLevel { get => _driversLevel; private set => SetField(ref _driversLevel, value); }
    private string _driversText = "Controleren…";
    private string _driversLevel = Level.Learning;

    /// <summary>Short text for the tray icon.</summary>
    public string TrayText => Controllers.Count switch
    {
        0 => "DS4 Handler – geen controller",
        1 => "DS4 Handler – 1 controller actief",
        var n => $"DS4 Handler – {n} controllers actief",
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
                DriverChip = "Xbox-driver OK"; DriverWord = "werkt"; DriverLevel = Level.Ok; DriverTip = Tips.DriverReady; break;
            case VirtualDriverState.Missing:
                DriverChip = "Xbox-driver ontbreekt"; DriverWord = "ontbreekt"; DriverLevel = Level.Bad; DriverTip = Tips.DriverMissing; break;
            case VirtualDriverState.Failed:
                DriverChip = "Xbox-driver fout"; DriverWord = "fout"; DriverLevel = Level.Bad; DriverTip = "ViGEmBus kon niet worden geopend: " + manager.DriverError; break;
            default:
                DriverChip = "Controleren…"; DriverWord = "controleren…"; DriverLevel = Level.Learning; DriverTip = Tips.DriverChecking; break;
        }

        if (!hidHideEnabled)
        {
            HidHideChip = "HidHide uit"; HidHideWord = "uit"; HidHideLevel = Level.Off;
            HidHideTip = "Verbergen voor games staat uit (zie Instellingen). Sommige games zien dan twee controllers.";
        }
        else
        {
            switch (hidHide.Availability)
            {
                case HidHideAvailability.Ready when hidHide.Problem == null:
                    HidHideChip = "HidHide actief"; HidHideWord = "werkt"; HidHideLevel = Level.Ok;
                    HidHideTip = "HidHide verbergt je echte controllers voor games, zodat ze alleen de Xbox-controller zien.";
                    break;
                case HidHideAvailability.Ready:
                    HidHideChip = "HidHide: let op"; HidHideWord = "let op"; HidHideLevel = Level.Warn; HidHideTip = hidHide.Problem!; break;
                case HidHideAvailability.NotInstalled:
                    HidHideChip = "HidHide ontbreekt"; HidHideWord = "niet gevonden"; HidHideLevel = Level.Warn;
                    HidHideTip = "HidHide is niet geïnstalleerd. Zonder HidHide zien sommige games je controller dubbel."; break;
                case HidHideAvailability.Busy:
                    HidHideChip = "HidHide bezet"; HidHideWord = "bezet"; HidHideLevel = Level.Warn;
                    HidHideTip = "Een ander programma (HidHide Configuration Client of DS4Windows) heeft HidHide open. Sluit dat en klik op Opnieuw controleren."; break;
                case HidHideAvailability.Error:
                    HidHideChip = "HidHide fout"; HidHideWord = "fout"; HidHideLevel = Level.Warn;
                    HidHideTip = "HidHide reageerde niet zoals verwacht. Zie het logbestand (Instellingen)."; break;
                default:
                    HidHideChip = "Controleren…"; HidHideWord = "controleren…"; HidHideLevel = Level.Learning; HidHideTip = "Bezig met controleren of HidHide aanwezig is."; break;
            }
        }

        // One line for the top bar: the most important problem, otherwise "in orde".
        if (DriverLevel is Level.Bad or Level.Warn) { DriversText = DriverChip; DriversLevel = DriverLevel; }
        else if (HidHideLevel is Level.Bad or Level.Warn) { DriversText = HidHideChip; DriversLevel = HidHideLevel; }
        else if (DriverLevel == Level.Learning || HidHideLevel == Level.Learning) { DriversText = "Controleren…"; DriversLevel = Level.Learning; }
        else { DriversText = "Stuurprogramma's in orde"; DriversLevel = Level.Ok; }

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
                Title = "De Xbox-driver (ViGEmBus) ontbreekt",
                Text = "Zonder ViGEmBus kan DS4 Handler geen Xbox-controller maken, dus games krijgen geen input. Je ziet hier wel alle waarden. Installeer ViGEmBus (gratis) en start de app daarna opnieuw.",
                PrimaryText = "ViGEmBus downloaden",
                PrimaryCommand = new RelayCommand(() => AppHost.OpenUrl(AppHost.ViGEmDownloadUrl)),
                PrimaryTip = Tips.DownloadViGEm,
            });
        }
        else if (manager.DriverState == VirtualDriverState.Failed)
        {
            list.Add(new NoticeViewModel
            {
                Level = Level.Bad,
                Title = "De Xbox-driver (ViGEmBus) werkt niet",
                Text = $"ViGEmBus kon niet worden geopend ({manager.DriverError}). Herstart de pc; helpt dat niet, installeer ViGEmBus dan opnieuw.",
                PrimaryText = "ViGEmBus downloaden",
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
                        Title = "HidHide is niet geïnstalleerd",
                        Text = "Zonder HidHide zien sommige games zowel je PlayStation-controller als de Xbox-controller, waardoor knoppen dubbel reageren. Installeer HidHide (gratis) en klik daarna op Opnieuw controleren. Geen last van dubbele input? Zet HidHide dan uit bij Instellingen.",
                        PrimaryText = "HidHide downloaden",
                        PrimaryCommand = new RelayCommand(() => AppHost.OpenUrl(HidHideService.DownloadUrl)),
                        PrimaryTip = Tips.DownloadHidHide,
                        SecondaryText = "Opnieuw controleren",
                        SecondaryCommand = recheck,
                        SecondaryTip = Tips.RecheckHidHide,
                    });
                    break;
                case HidHideAvailability.Busy:
                    list.Add(new NoticeViewModel
                    {
                        Level = Level.Warn,
                        Title = "HidHide is bezet",
                        Text = "Een ander programma heeft HidHide open, meestal de HidHide-configuratie of DS4Windows. Sluit dat programma en klik op Opnieuw controleren.",
                        PrimaryText = "Opnieuw controleren",
                        PrimaryCommand = recheck,
                        PrimaryTip = Tips.RecheckHidHide,
                    });
                    break;
                case HidHideAvailability.Error:
                    list.Add(new NoticeViewModel
                    {
                        Level = Level.Warn,
                        Title = "HidHide reageert niet",
                        Text = "HidHide gaf een onverwachte fout. Herstart de pc of installeer HidHide opnieuw.",
                        PrimaryText = "Opnieuw controleren",
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
                    Title = "Controller kon niet worden verborgen",
                    Text = problem,
                    PrimaryText = "Opnieuw proberen",
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
                Title = blocked == 1 ? "Een controller is in gebruik door een ander programma" : $"{blocked} controllers zijn in gebruik door een ander programma",
                Text = "DS4 Handler ziet een controller maar kan hem niet openen. Sluit DS4Windows of andere controller-software. De app probeert het elke paar seconden opnieuw.",
                PrimaryText = "Logbestand openen",
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
        Updates = new UpdateViewModel(host);
    }

    /// <summary>"Afsluiten": quits the app completely (the main window forwards this to the app).</summary>
    public ICommand QuitCommand { get; }

    /// <summary>Raised by <see cref="QuitCommand"/>.</summary>
    public event EventHandler? QuitRequested;

    public ICommand RecheckHidHideCommand { get; }
    public ICommand DownloadHidHideCommand { get; }
    public ICommand DownloadViGEmCommand { get; }
    public ICommand OpenProfilesCommand { get; }
    public ICommand OpenLogCommand { get; }

    /// <summary>The Updates block on this page; also feeds the update banner of the main window.</summary>
    public UpdateViewModel Updates { get; }

    public string Version =>
        "Versie " + UpdateService.CurrentVersion;

    public string ProfileFolder => _host.Profiles.Directory;

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
            HidHideAvailability.Ready => ("HidHide is geïnstalleerd en klaar voor gebruik.", Level.Ok),
            HidHideAvailability.NotInstalled => ("HidHide is niet geïnstalleerd. Download het gratis via de knop hieronder en klik daarna op Opnieuw controleren.", Level.Warn),
            HidHideAvailability.Busy => ("HidHide is bezet: sluit de HidHide-configuratie (of DS4Windows) en klik op Opnieuw controleren.", Level.Warn),
            HidHideAvailability.Error => ("HidHide gaf een onverwachte fout. Zie het logbestand.", Level.Bad),
            _ => ("Bezig met controleren…", Level.Learning),
        };
    }
}
