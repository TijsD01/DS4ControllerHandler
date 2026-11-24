using System.Windows.Input;
using DS4Handler.App.Controls;
using DS4Handler.App.Engine;
using DS4Handler.App.Lang;
using DS4Handler.App.Services;
using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;

namespace DS4Handler.App.ViewModels;

/// <summary>Status colour of a dot or line: "ok", "learning", "warn", "bad", "off", "info".</summary>
public static class Level
{
    /// <summary>Neutral information (blue), e.g. which profile a controller uses.</summary>
    public const string Info = "info";
    public const string Ok = "ok";
    public const string Learning = "learning";
    public const string Warn = "warn";
    public const string Bad = "bad";
    public const string Off = "off";
}

internal static class Fmt
{
    public static string Pct(double fraction) => Math.Round(fraction * 100).ToString("0", Loc.Culture) + "%";

    public static string Signed(double v) => v.ToString("+0.00;−0.00;0.00", Loc.Culture);
}

internal static class Words
{
    /// <summary>The least healthy of two levels (bad, warn, learning, ok, off).</summary>
    public static string Worst(string a, string b)
    {
        static int Rank(string l) => l switch { Level.Bad => 4, Level.Warn => 3, Level.Learning => 2, Level.Ok => 1, _ => 0 };
        return Rank(a) >= Rank(b) ? a : b;
    }
}

/// <summary>Live view and learned facts of one stick.</summary>
public sealed class StickViewModel : ObservableObject
{
    private static readonly StickCalibrationOptions Defaults = new();
    private double _rawX, _rawY, _outX, _outY, _centerX, _centerY, _deadzone;
    private bool _guided;
    private string _valueText = "";
    private string _summary = "";
    private string _summaryLevel = Level.Learning;
    private string _deadzoneTip = Tips.Deadzone;

    public StickViewModel(string name, string shortName)
    {
        Name = name;
        ShortName = shortName;
    }

    /// <summary>"Left stick" / "Right stick" (Calibration, summaries).</summary>
    public string Name { get; }

    /// <summary>"Left" / "Right" (label under the live stick).</summary>
    public string ShortName { get; }

    public double RawX { get => _rawX; private set => SetNumber(ref _rawX, value); }
    public double RawY { get => _rawY; private set => SetNumber(ref _rawY, value); }
    public double OutX { get => _outX; private set => SetNumber(ref _outX, value); }
    public double OutY { get => _outY; private set => SetNumber(ref _outY, value); }
    public double CenterX { get => _centerX; private set => SetNumber(ref _centerX, value); }
    public double CenterY { get => _centerY; private set => SetNumber(ref _centerY, value); }
    public double Deadzone { get => _deadzone; private set => SetNumber(ref _deadzone, value); }
    public bool Guided { get => _guided; private set => SetField(ref _guided, value); }
    public string ValueText { get => _valueText; private set => SetField(ref _valueText, value); }
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }
    public string SummaryLevel { get => _summaryLevel; private set => SetField(ref _summaryLevel, value); }
    public string DeadzoneTip { get => _deadzoneTip; private set => SetField(ref _deadzoneTip, value); }

    internal void Update(byte rawX, byte rawY, float outX, float outY, in StickLive live)
    {
        RawX = (rawX - 127.5) / 127.5;
        RawY = -(rawY - 127.5) / 127.5;
        OutX = outX;
        OutY = outY;
        CenterX = live.CenterX;
        CenterY = live.CenterY;
        Deadzone = live.Deadzone;
        Guided = live.GuidedInProgress;
        ValueText = $"x {Fmt.Signed(outX)} · y {Fmt.Signed(outY)}";
    }

    internal void UpdateSummary(StickSummary s, bool guided)
    {
        DeadzoneTip = Tips.Deadzone + "\n\n" + Loc.Format("Calib_DeadzoneNow", Fmt.Pct(s.Deadzone));
        if (guided)
        {
            Summary = Loc.Format("Calib_StickGuided", Name);
            SummaryLevel = Level.Learning;
            return;
        }

        var text = new System.Text.StringBuilder();
        double drift = Math.Sqrt(s.CenterX * s.CenterX + s.CenterY * s.CenterY);
        bool learning = s.Releases < Defaults.BootstrapReleases;
        if (learning)
            text.Append(Loc.Format("Calib_StickLearning", Name));
        else if (drift >= 0.02)
            text.Append(Loc.Format("Calib_StickRestsAt", Name, Fmt.Signed(s.CenterX), Fmt.Signed(s.CenterY)));
        else
            text.Append(Loc.Format("Calib_StickCentered", Name));

        text.Append(' ').Append(Loc.Format("Calib_StickDeadzone", Fmt.Pct(s.Deadzone)));

        if (s.WeakestReach is { } reach && reach < 0.97)
            text.Append(' ').Append(Loc.Format("Calib_StickWeakReach", Fmt.Pct(reach)));

        if (s.JitterFilterStrength > 0)
        {
            string jitter = "±" + (s.Jitter * 100).ToString(s.Jitter < 0.1 ? "0.0" : "0", Loc.Culture) + "%";
            string name = Name.ToLower(Loc.Culture);
            text.Append(' ').Append(s.JitterFilterEnabled
                ? Loc.Format("Calib_StickJitterFiltered", name, jitter)
                : Loc.Format("Calib_StickJitterOff", name, jitter));
        }

        Summary = text.ToString();
        SummaryLevel = learning ? Level.Learning : Level.Ok;
    }
}

/// <summary>Live view, learned facts and guided assistant of one trigger.</summary>
public sealed class TriggerViewModel : ObservableObject
{
    private readonly ControllerSession _session;
    private readonly bool _right;
    private double _raw, _output, _endStop = 1, _rest;
    private string _valueText = "";
    private string _summary = "";
    private string _summaryLevel = Level.Learning;
    private bool _guided;
    private string _guidedText = "";
    private string _barTip = Tips.TriggerBar;

    public TriggerViewModel(ControllerSession session, bool right)
    {
        _session = session;
        _right = right;
        Name = right ? "R2" : "L2";
        StartGuidedCommand = new RelayCommand(() => _session.StartGuidedEndStop(_right));
        CancelGuidedCommand = new RelayCommand(() => _session.CancelGuidedEndStop(_right));
    }

    public string Name { get; }
    public string CalibrateText => Loc.Format("Calib_CalibrateTrigger", Name);

    /// <summary>Short status for the Kalibratie summary on Live: "L2 in orde", "R2 leert nog".</summary>
    public ICommand StartGuidedCommand { get; }
    public ICommand CancelGuidedCommand { get; }

    public double Raw { get => _raw; private set => SetNumber(ref _raw, value); }
    public double Output { get => _output; private set => SetNumber(ref _output, value); }
    public double EndStop { get => _endStop; private set => SetNumber(ref _endStop, value); }
    public double Rest { get => _rest; private set => SetNumber(ref _rest, value); }
    public string ValueText { get => _valueText; private set => SetField(ref _valueText, value); }
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }
    public string SummaryLevel { get => _summaryLevel; private set => SetField(ref _summaryLevel, value); }
    public bool GuidedActive { get => _guided; private set => SetField(ref _guided, value); }
    public string GuidedText { get => _guidedText; private set => SetField(ref _guidedText, value); }
    public string BarTip { get => _barTip; private set => SetField(ref _barTip, value); }

    internal void Update(byte raw, float output, in TriggerLive live)
    {
        Raw = raw / 255.0;
        Output = output;
        EndStop = live.EndStop / 255.0;
        Rest = live.Rest / 255.0;
        ValueText = $"{Fmt.Pct(raw / 255.0)} → {Fmt.Pct(output)}";
        GuidedActive = live.GuidedInProgress;
        GuidedText = live.GuidedInProgress
            ? (live.GuidedPressesRemaining > 0
                ? Loc.Format("Calib_TriggerPressMore", Name, live.GuidedPressesRemaining)
                : Loc.Format("Calib_TriggerRelease", Name))
            : "";
    }

    internal void UpdateSummary(in TriggerLive t)
    {
        double reach = t.EndStop / 255.0;
        BarTip = Tips.TriggerBar + "\n\n" + Loc.Format("Calib_TriggerLearnedEnd", Fmt.Pct(reach));

        if (t.GuidedInProgress)
        {
            Summary = Loc.Format("Calib_TriggerGuided", Name);
            SummaryLevel = Level.Learning;
            return;
        }

        string text;
        string level = Level.Ok;
        if (!t.EndStopLearned)
        {
            text = Loc.Format("Calib_TriggerLearning", Name);
            level = Level.Learning;
        }
        else
        {
            string how = t.EndStopKind == PlateauKind.Guided
                ? Loc.Get("Calib_TriggerSetByAssistant")
                : Loc.Get("Calib_TriggerLearnedAuto");
            text = t.EndStop >= 250
                ? Loc.Format("Calib_TriggerFullRange", Name, how)
                : Loc.Format("Calib_TriggerScaled", Name, Fmt.Pct(reach), how);
        }

        if (t.Rest > 4) text += " " + Loc.Format("Calib_TriggerRestIgnored", Fmt.Pct(t.Rest / 255.0));
        Summary = text;
        SummaryLevel = level;
    }
}

/// <summary>One controller card.</summary>
public sealed class ControllerViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly Func<string, string, string, bool> _confirm;
    private ControllerSnapshot _snap;
    private long _lastReports;
    private long _lastRateTimestamp;
    private int _tick;
    private bool _learning;
    private bool _jitterFilter;
    private bool _hide;
    private bool _hideAvailable;
    private string _battery = "";
    private string _batteryTip = "";
    private bool _hasBattery;
    private string _status = "";
    private string _statusLevel = Level.Learning;
    private string _statusTip = "";
    private string _hideStatus = "";
    private string _hideLevel = Level.Off;
    private string _hideStatusTip = "";
    private string _rate = "";
    private string _latencyText = "", _latencyTip = "";
    private bool _sticksGuided;
    private bool _anyInput;
    private bool _isShown;
    private string _tabMeta = "";
    private int _selectedTab;
    private string _sticksLevel = Level.Learning;
    private OutputMode _output;
    private string _lightBar;
    private bool _showOutput;
    private Buttons _rawButtons;
    private Buttons _outButtons;

    public ControllerViewModel(ControllerSession session, AppHost host, Func<string, string, string, bool> confirm)
    {
        Session = session;
        _host = host;
        _confirm = confirm;
        Left = new StickViewModel(Loc.Get("Stick_Left"), Loc.Get("Stick_LeftShort"));
        Right = new StickViewModel(Loc.Get("Stick_Right"), Loc.Get("Stick_RightShort"));
        L2 = new TriggerViewModel(session, right: false);
        R2 = new TriggerViewModel(session, right: true);
        _learning = session.LearningEnabled;
        _jitterFilter = host.SettingsStore.Read(s => s.For(session.Id).JitterFilterEnabled);
        _hide = host.SettingsStore.Read(s => s.For(session.Id).HideFromGames);
        _output = host.SettingsStore.Read(s => s.For(session.Id).Output);
        _lightBar = $"#{OutputReports.ParseLightBar(host.SettingsStore.Read(s => s.For(session.Id).LightBar)):X6}";
        _tabMeta = IsBluetooth ? "BT" : "USB";

        Tuning = new TuningViewModel(session, host);
        Remap = new RemapViewModel(session, host, this, _output == OutputMode.Xbox360, confirm);
        ResetCommand = new RelayCommand(Reset);
        RecenterCommand = new RelayCommand(() => Session.StartGuidedCenter());
        CancelRecenterCommand = new RelayCommand(() => Session.CancelGuidedCenter());
        ShowCalibrationCommand = new RelayCommand(() => SelectedTab = TabCalibration);
    }

    private const int TabCalibration = 1, TabButtons = 3;

    public ControllerSession Session { get; }
    public StickViewModel Left { get; }
    public StickViewModel Right { get; }
    public TriggerViewModel L2 { get; }
    public TriggerViewModel R2 { get; }

    /// <summary>The "Afstellen" tab (deadzone shapes, curves, poll rate, ...).</summary>
    public TuningViewModel Tuning { get; }

    /// <summary>The "Knoppen" tab (button remapping, keyboard keys, swapping the sticks).</summary>
    public RemapViewModel Remap { get; }

    public ICommand ResetCommand { get; }
    public ICommand RecenterCommand { get; }
    public ICommand CancelRecenterCommand { get; }

    /// <summary>Switches the page to the Kalibratie tab (the summary row on Live).</summary>
    public ICommand ShowCalibrationCommand { get; }

    /// <summary>View state: the tab shown on this controller's page (0 Live, 1 Kalibratie, 2 Afstellen, 3 Knoppen). Not saved.</summary>
    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetField(ref _selectedTab, Math.Clamp(value, 0, TabButtons)) && _selectedTab != TabButtons)
                Remap.CancelCapture(); // stop listening for a key once the tab is left
        }
    }

    /// <summary>Both sticks together, for the Kalibratie summary on Live: the least healthy of the two.</summary>
    public string SticksLevel { get => _sticksLevel; private set => SetField(ref _sticksLevel, value); }

    private string? _title;
    private string _profileText = "";
    private string _profileLevel = Level.Off;
    private string _profileTip = "";

    /// <summary>Friendly name from the Profielen tab, or the model name.</summary>
    public string Title { get => _title ?? Session.DisplayName; private set => SetField(ref _title, value); }

    /// <summary>Which profile this controller learns into: "Profiel: eigen profiel", "Profiel: …" or a blocked warning.</summary>
    public string ProfileText { get => _profileText; private set => SetField(ref _profileText, value); }
    public string ProfileLevel { get => _profileLevel; private set => SetField(ref _profileLevel, value); }
    public string ProfileTip { get => _profileTip; private set => SetField(ref _profileTip, value); }
    public string IdText => Session.Mac != null ? $"MAC {Session.Mac}" : Loc.Get("Profile_NoMacFound");
    public string IdTip => Session.HasStableId ? Tips.ControllerId : Tips.ControllerIdUnstable;
    public bool IsBluetooth => Session.Connection == ConnectionType.Bluetooth;
    public string ConnectionText => IsBluetooth ? "Bluetooth" : "USB";
    public string ConnectionTip => IsBluetooth ? Tips.Bluetooth : Tips.Usb;

    /// <summary>Mono line on the controller's tab in the top bar: "BT · 82%" or "USB".</summary>
    public string TabMeta { get => _tabMeta; private set => SetField(ref _tabMeta, value); }

    /// <summary>This controller's page is the one on screen (drives the top-bar tab highlight).</summary>
    public bool IsShown { get => _isShown; internal set => SetField(ref _isShown, value); }

    public string BatteryText { get => _battery; private set => SetField(ref _battery, value); }
    public string BatteryTip { get => _batteryTip; private set => SetField(ref _batteryTip, value); }
    public bool HasBattery { get => _hasBattery; private set => SetField(ref _hasBattery, value); }
    public string StatusText { get => _status; private set => SetField(ref _status, value); }
    public string StatusLevel { get => _statusLevel; private set => SetField(ref _statusLevel, value); }
    public string StatusTip { get => _statusTip; private set => SetField(ref _statusTip, value); }
    public string HideStatusText { get => _hideStatus; private set => SetField(ref _hideStatus, value); }
    public string HideLevel { get => _hideLevel; private set => SetField(ref _hideLevel, value); }
    public string HideStatusTip { get => _hideStatusTip; private set { if (SetField(ref _hideStatusTip, value)) OnPropertyChanged(nameof(HideChipTip)); } }

    /// <summary>Tooltip of the "Verborgen voor games" chip: what the switch does, plus the current state.</summary>
    public string HideChipTip => string.IsNullOrEmpty(HideStatusTip) ? HideTip : HideTip + "\n\n" + Loc.Format("Common_Now", HideStatusTip);
    public string ReportRateText { get => _rate; private set => SetField(ref _rate, value); }

    /// <summary>"verwerking 0,04 ms" for the header; empty until the first second of reports is measured.</summary>
    public string LatencyText { get => _latencyText; private set => SetField(ref _latencyText, value); }

    /// <summary>Tooltip with the details behind <see cref="LatencyText"/>.</summary>
    public string LatencyTip { get => _latencyTip; private set => SetField(ref _latencyTip, value); }
    public bool SticksGuided { get => _sticksGuided; private set => SetField(ref _sticksGuided, value); }
    public bool HasInput { get => _anyInput; private set => SetField(ref _anyInput, value); }

    /// <summary>The hide switch is only usable while HidHide is switched on in the settings.</summary>
    public bool HideAvailable { get => _hideAvailable; private set => SetField(ref _hideAvailable, value); }
    public string HideTip => HideAvailable ? Tips.HideFromGames : Tips.HideUnavailable;

    public bool LearningEnabled
    {
        get => _learning;
        set
        {
            if (!SetField(ref _learning, value)) return;
            _host.SetLearning(Session, value);
        }
    }

    /// <summary>Stick jitter filter (both sticks) on or off.</summary>
    public bool JitterFilterEnabled
    {
        get => _jitterFilter;
        set
        {
            if (!SetField(ref _jitterFilter, value)) return;
            _host.SetJitterFilter(Session, value);
            _tick = 0; // refresh the summary soon
        }
    }

    /// <summary>Light bar colours to pick from; the first is the app's blue. Black switches it off.</summary>
    public static IReadOnlyList<string> LightBarColors { get; } = new[]
    {
        "#0030FF", "#00C8FF", "#00FF40", "#FFE000", "#FF6000", "#FF0000", "#FF00A0", "#8000FF", "#FFFFFF", "#000000",
    };

    /// <summary>Only a DualShock 4 gets its light bar set by the app.</summary>
    public bool HasLightBar => Session.Model == ControllerModel.DualShock4;

    /// <summary>Chosen light bar colour, "#RRGGBB".</summary>
    public string LightBarColor
    {
        get => _lightBar;
        set
        {
            if (value == null || !SetField(ref _lightBar, value)) return;
            _host.SetLightBar(Session, value);
        }
    }

    public bool OutputXbox
    {
        get => _output == OutputMode.Xbox360;
        set { if (value) SetOutput(OutputMode.Xbox360); }
    }

    public bool OutputDs4
    {
        get => _output == OutputMode.DualShock4;
        set { if (value) SetOutput(OutputMode.DualShock4); }
    }

    private void SetOutput(OutputMode mode)
    {
        if (_output == mode) return;
        _output = mode;
        OnPropertyChanged(nameof(OutputXbox));
        OnPropertyChanged(nameof(OutputDs4));
        OnPropertyChanged(nameof(OutputKind));
        Remap.SetOutputXbox(mode == OutputMode.Xbox360);
        _host.SetOutputMode(Session, mode);
        _tick = 0; // refresh the status pill soon
    }

    /// <summary>View state of the KNOPPEN card: false shows the real controller, true what the game gets. Not saved.</summary>
    public bool ShowOutput
    {
        get => _showOutput;
        set { if (SetField(ref _showOutput, value)) OnPropertyChanged(nameof(ShowInput)); }
    }

    /// <summary>Inverse of <see cref="ShowOutput"/>, for the "Controller" segment.</summary>
    public bool ShowInput
    {
        get => !_showOutput;
        set => ShowOutput = !value;
    }

    public PadKind InputKind => Session.Model == ControllerModel.DualShock4 ? PadKind.DualShock4 : PadKind.DualSense;
    public PadKind OutputKind => OutputXbox ? PadKind.Xbox360 : PadKind.DualShock4;
    public Buttons RawButtons { get => _rawButtons; private set => SetField(ref _rawButtons, value); }
    public Buttons OutButtons { get => _outButtons; private set => SetField(ref _outButtons, value); }

    public bool HideFromGames
    {
        get => _hide;
        set
        {
            if (!SetField(ref _hide, value)) return;
            _host.SetHideFromGames(Session, value);
        }
    }

    /// <summary>Called ~30 times per second by the main view model.</summary>
    public void Tick()
    {
        Session.CopySnapshot(ref _snap);
        HasInput = _snap.HasInput;
        var raw = _snap.Raw;
        var output = _snap.Output;
        Left.Update(raw.LeftX, raw.LeftY, output.LeftX, output.LeftY, _snap.Left);
        Right.Update(raw.RightX, raw.RightY, output.RightX, output.RightY, _snap.Right);
        L2.Update(raw.L2, output.L2, _snap.L2);
        R2.Update(raw.R2, output.R2, _snap.R2);
        RawButtons = raw.Buttons;
        OutButtons = output.Buttons;
        Remap.Update(raw.Buttons, raw.L2, raw.R2, _selectedTab == TabButtons && _isShown);
        SticksGuided = _snap.Left.GuidedInProgress || _snap.Right.GuidedInProgress;

        // Slower things: twice per second.
        if (_tick++ % 15 != 0) return;
        if (_learning != _snap.LearningEnabled && _snap.HasInput) { _learning = _snap.LearningEnabled; OnPropertyChanged(nameof(LearningEnabled)); }
        UpdateRate();
        UpdateBattery(raw.BatteryPercent);
        UpdateStatus();
        UpdateHide();
        UpdateProfile();
        Tuning.Refresh(); // picks up a profile switch without applying it back
        Remap.Refresh();
        if (Session.GetSummary() is { } summary)
        {
            Left.UpdateSummary(summary.Left, _snap.Left.GuidedInProgress);
            Right.UpdateSummary(summary.Right, _snap.Right.GuidedInProgress);
        }
        L2.UpdateSummary(_snap.L2);
        R2.UpdateSummary(_snap.R2);
        SticksLevel = Words.Worst(Left.SummaryLevel, Right.SummaryLevel);
    }

    /// <summary>Learning switched from the tray menu.</summary>
    public void RefreshLearning()
    {
        _learning = Session.LearningEnabled;
        OnPropertyChanged(nameof(LearningEnabled));
    }

    private void UpdateRate()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastRateTimestamp != 0)
        {
            double seconds = (now - _lastRateTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency;
            double rate = seconds > 0 ? (_snap.Reports - _lastReports) / seconds : 0;
            ReportRateText = $"{rate:0} Hz";
        }
        var l = _snap.Latency;
        bool recent = _snap.LastReportTimestamp != 0 &&
                      now - _snap.LastReportTimestamp < 2 * System.Diagnostics.Stopwatch.Frequency;
        if (l.ProcessAvgMicros > 0 && _snap.HasInput && recent)
        {
            LatencyText = Loc.Format("Latency_Text", Ms(l.ProcessAvgMicros / 1000));
            LatencyTip = Tips.Latency + "\n\n" + Loc.Format("Latency_Now",
                Ms(l.ProcessAvgMicros / 1000), Ms(l.ProcessMaxMicros / 1000), l.GapMaxMs.ToString("0.0", Loc.Culture));
        }
        else
        {
            LatencyText = "";
            LatencyTip = "";
        }
        _lastRateTimestamp = now;
        _lastReports = _snap.Reports;
    }

    private static string Ms(float ms) => ms.ToString(ms < 1 ? "0.00" : "0.0", Loc.Culture);

    private void UpdateBattery(byte? percent)
    {
        HasBattery = percent.HasValue;
        TabMeta = IsBluetooth && percent is { } b ? $"BT · {b}%" : IsBluetooth ? "BT" : "USB";
        if (percent is not { } p) return;
        BatteryText = $"{p}%";
        BatteryTip = IsBluetooth
            ? (p <= 20 ? Loc.Format("Battery_Low", p) : Loc.Format("Battery_Wireless", p))
            : Loc.Format("Battery_Usb", p);
    }

    private void UpdateStatus()
    {
        bool stale = _snap.HasInput && (System.Diagnostics.Stopwatch.GetTimestamp() - _snap.LastReportTimestamp)
            > System.Diagnostics.Stopwatch.Frequency * 2;
        switch (Session.VirtualState)
        {
            case VirtualPadState.Active when !_snap.HasInput || stale:
                StatusText = Loc.Get("Status_WaitingInput");
                StatusLevel = Level.Learning;
                StatusTip = Loc.Get("Status_WaitingInputTip");
                break;
            case VirtualPadState.Active when Session.OutputMode == OutputMode.DualShock4:
                StatusText = Loc.Get("Status_Ds4Active");
                StatusLevel = Level.Ok;
                StatusTip = Loc.Get("Status_Ds4ActiveTip");
                break;
            case VirtualPadState.Active:
                StatusText = Loc.Get("Status_XboxActive");
                StatusLevel = Level.Ok;
                StatusTip = Loc.Get("Status_XboxActiveTip");
                break;
            case VirtualPadState.DriverMissing:
                StatusText = Loc.Get("Status_MeasureOnly");
                StatusLevel = Level.Warn;
                StatusTip = Loc.Get("Status_MeasureOnlyTip");
                break;
            case VirtualPadState.Failed:
                StatusText = Loc.Get("Status_VirtualFailed");
                StatusLevel = Level.Bad;
                // VirtualError is the driver's own exception text (ViGEm), shown as is.
                StatusTip = Loc.Format("Status_VirtualFailedTip", Session.VirtualError ?? Loc.Get("Status_UnknownError"));
                break;
            default:
                StatusText = Loc.Get("Status_Starting");
                StatusLevel = Level.Learning;
                StatusTip = Loc.Get("Status_StartingTip");
                break;
        }
    }

    private void UpdateProfile()
    {
        var library = _host.Library;
        Title = library.DisplayName(Session.Id);
        if (Session.ProfileBlocked)
        {
            ProfileText = Loc.Get("Profile_CardBlocked");
            ProfileLevel = Level.Warn;
            ProfileTip = Tips.ProfileBlockedOnCard;
        }
        else if (!string.Equals(Session.ProfileId, Session.Id, StringComparison.OrdinalIgnoreCase))
        {
            ProfileText = Loc.Format("Profile_CardShared", library.DisplayName(Session.ProfileId));
            ProfileLevel = Level.Info;
            ProfileTip = Tips.ProfileOnCard;
        }
        else
        {
            ProfileText = Loc.Get("Profile_CardOwn");
            ProfileLevel = Level.Info;
            ProfileTip = Tips.ProfileOwnOnCard;
        }
    }

    private void UpdateHide()
    {
        bool enabled = _host.SettingsStore.Read(s => s.HidHideEnabled);
        if (HideAvailable != enabled)
        {
            HideAvailable = enabled;
            OnPropertyChanged(nameof(HideTip));
            OnPropertyChanged(nameof(HideChipTip));
        }
        switch (_host.HidHide.StateFor(Session.Id))
        {
            case DeviceHideState.Hidden:
                HideStatusText = Loc.Get("Hide_Hidden");
                HideLevel = Level.Ok;
                HideStatusTip = Loc.Get("Hide_HiddenTip");
                break;
            case DeviceHideState.Working:
                HideStatusText = Loc.Get("Hide_Working");
                HideLevel = Level.Learning;
                HideStatusTip = Loc.Get("Hide_WorkingTip");
                break;
            case DeviceHideState.Failed:
                HideStatusText = Loc.Get("Hide_Failed");
                HideLevel = Level.Warn;
                HideStatusTip = Loc.Format("Hide_FailedTip", _host.HidHide.Problem ?? Loc.Get("Hide_FailedSeeNotice"));
                break;
            default:
                HideStatusText = "";
                HideLevel = Level.Off;
                HideStatusTip = "";
                break;
        }
    }

    private void Reset()
    {
        if (!_confirm(
                Loc.Get("Calib_ResetTitle"),
                Loc.Format("Calib_ResetMessage", Title),
                Loc.Get("Calib_ResetConfirm")))
            return;
        Session.ResetCalibration();
        _tick = 0; // refresh the summary on the next tick
    }
}
