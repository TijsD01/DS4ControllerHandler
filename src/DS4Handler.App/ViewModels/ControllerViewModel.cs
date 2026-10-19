using System.Globalization;
using System.Windows.Input;
using DS4Handler.App.Engine;
using DS4Handler.App.Services;
using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;

namespace DS4Handler.App.ViewModels;

/// <summary>Status colour of a pill or line: "ok", "learning", "warn", "bad", "off".</summary>
public static class Level
{
    public const string Ok = "ok";
    public const string Learning = "learning";
    public const string Warn = "warn";
    public const string Bad = "bad";
    public const string Off = "off";
}

internal static class Fmt
{
    public static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static string Pct(double fraction) => Math.Round(fraction * 100).ToString("0", Nl) + "%";

    public static string Signed(double v) => v.ToString("+0.00;−0.00;0.00", Nl);
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

    public StickViewModel(string name) => Name = name;

    public string Name { get; }

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
        ValueText = $"{Fmt.Signed(outX)}  {Fmt.Signed(outY)}";
    }

    internal void UpdateSummary(StickSummary s, bool guided)
    {
        DeadzoneTip = Tips.Deadzone + $"\n\nNu: {Fmt.Pct(s.Deadzone)} van de stickuitslag.";
        if (guided)
        {
            Summary = $"{Name}: laat de stick los… het rustpunt wordt gemeten.";
            SummaryLevel = Level.Learning;
            return;
        }

        var text = new System.Text.StringBuilder();
        double drift = Math.Sqrt(s.CenterX * s.CenterX + s.CenterY * s.CenterY);
        bool learning = s.Releases < Defaults.BootstrapReleases;
        if (learning)
            text.Append($"{Name}: nog aan het leren — laat de stick af en toe even los. ");
        else if (drift >= 0.02)
            text.Append($"{Name} rust op ({Fmt.Signed(s.CenterX)}; {Fmt.Signed(s.CenterY)}) in plaats van in het midden — dat punt telt nu als midden. ");
        else
            text.Append($"{Name} rust netjes in het midden. ");

        text.Append($"Deadzone {Fmt.Pct(s.Deadzone)}.");

        if (s.WeakestReach is { } reach && reach < 0.97)
            text.Append($" Haalt in sommige richtingen maar {Fmt.Pct(reach)} van de rand — wordt bijgeschaald naar 100%.");

        if (s.JitterFilterStrength > 0)
        {
            string jitter = "±" + (s.Jitter * 100).ToString(s.Jitter < 0.1 ? "0.0" : "0", Fmt.Nl) + "%";
            text.Append(s.JitterFilterEnabled
                ? $" Trilling {Name.ToLower(Fmt.Nl)}: {jitter} (wordt weggefilterd)."
                : $" Trilling {Name.ToLower(Fmt.Nl)}: {jitter} (trillingsfilter staat uit).");
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
    public string CalibrateText => $"Kalibreer {Name}";
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
        ValueText = $"ingedrukt {Fmt.Pct(raw / 255.0)} → game {Fmt.Pct(output)}";
        GuidedActive = live.GuidedInProgress;
        GuidedText = live.GuidedInProgress
            ? (live.GuidedPressesRemaining > 0
                ? $"Druk {Name} nog {live.GuidedPressesRemaining}× helemaal in"
                : $"Laat {Name} los…")
            : "";
    }

    internal void UpdateSummary(in TriggerLive t)
    {
        double reach = t.EndStop / 255.0;
        BarTip = Tips.TriggerBar + $"\n\nGeleerd eindpunt: {Fmt.Pct(reach)}.";

        if (t.GuidedInProgress)
        {
            Summary = $"{Name}: assistent loopt — druk de trigger een paar keer helemaal in.";
            SummaryLevel = Level.Learning;
            return;
        }

        string text;
        string level = Level.Ok;
        if (!t.EndStopLearned)
        {
            text = $"{Name}: nog aan het leren — druk hem tijdens het spelen een paar keer helemaal in. Tot dan geldt het volle bereik.";
            level = Level.Learning;
        }
        else
        {
            string how = t.EndStopKind == PlateauKind.Guided
                ? "Ingesteld met de assistent."
                : "Automatisch geleerd tijdens het spelen.";
            text = t.EndStop >= 250
                ? $"{Name} haalt het volle bereik — geen correctie nodig. {how}"
                : $"{Name} komt tot {Fmt.Pct(reach)} — wordt opgeschaald naar 100%. {how}";
        }

        if (t.Rest > 4) text += $" Rustwaarde {Fmt.Pct(t.Rest / 255.0)} wordt genegeerd.";
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
    private string _batteryIcon = "";
    private string _batteryTip = "";
    private bool _hasBattery;
    private string _status = "";
    private string _statusLevel = Level.Learning;
    private string _statusTip = "";
    private string _hideStatus = "";
    private string _hideLevel = Level.Off;
    private string _hideStatusTip = "";
    private string _rate = "";
    private bool _sticksGuided;
    private bool _anyInput;
    private bool _isShown;
    private string _sidebarMeta = "";
    private OutputMode _output;

    public ControllerViewModel(ControllerSession session, AppHost host, Func<string, string, string, bool> confirm)
    {
        Session = session;
        _host = host;
        _confirm = confirm;
        Left = new StickViewModel("Linker stick");
        Right = new StickViewModel("Rechter stick");
        L2 = new TriggerViewModel(session, right: false);
        R2 = new TriggerViewModel(session, right: true);
        _learning = session.LearningEnabled;
        _jitterFilter = host.SettingsStore.Read(s => s.For(session.Id).JitterFilterEnabled);
        _hide = host.SettingsStore.Read(s => s.For(session.Id).HideFromGames);
        _output = host.SettingsStore.Read(s => s.For(session.Id).Output);
        _sidebarMeta = $"{ModelShort} · {ConnectionText}";

        Tuning = new TuningViewModel(session, host);
        ResetCommand = new RelayCommand(Reset);
        RecenterCommand = new RelayCommand(() => Session.StartGuidedCenter());
        CancelRecenterCommand = new RelayCommand(() => Session.CancelGuidedCenter());
    }

    public ControllerSession Session { get; }
    public StickViewModel Left { get; }
    public StickViewModel Right { get; }
    public TriggerViewModel L2 { get; }
    public TriggerViewModel R2 { get; }

    /// <summary>The "Afstellen" tab (deadzone shapes, curves, poll rate, ...).</summary>
    public TuningViewModel Tuning { get; }

    public ICommand ResetCommand { get; }
    public ICommand RecenterCommand { get; }
    public ICommand CancelRecenterCommand { get; }

    private string? _title;
    private string _profileText = "";
    private string _profileLevel = Level.Off;
    private string _profileTip = "";

    /// <summary>Friendly name from the Profielen tab, or the model name.</summary>
    public string Title { get => _title ?? Session.DisplayName; private set => SetField(ref _title, value); }

    /// <summary>"Gebruikt profiel: …" when the controller learns into a shared profile (or is blocked); empty otherwise.</summary>
    public string ProfileText { get => _profileText; private set => SetField(ref _profileText, value); }
    public string ProfileLevel { get => _profileLevel; private set => SetField(ref _profileLevel, value); }
    public string ProfileTip { get => _profileTip; private set => SetField(ref _profileTip, value); }
    public string ModelIcon => ""; // game controller glyph
    public string IdText => Session.Mac != null ? $"MAC {Session.Mac}" : "(geen MAC gevonden)";
    public string IdTip => Session.HasStableId ? Tips.ControllerId : Tips.ControllerIdUnstable;
    public bool IsBluetooth => Session.Connection == ConnectionType.Bluetooth;
    public string ConnectionText => IsBluetooth ? "Bluetooth" : "USB";
    public string ConnectionIcon => IsBluetooth ? "" : "";
    public string ConnectionTip => IsBluetooth ? Tips.Bluetooth : Tips.Usb;

    /// <summary>Short model name for the sidebar ("DS4" / "DualSense").</summary>
    public string ModelShort => Session.Model == ControllerModel.DualShock4 ? "DS4" : "DualSense";

    /// <summary>Second line of the sidebar entry: "DS4 · Bluetooth · 42%".</summary>
    public string SidebarMeta { get => _sidebarMeta; private set => SetField(ref _sidebarMeta, value); }

    /// <summary>This controller's page is the one on screen (drives the sidebar highlight).</summary>
    public bool IsShown { get => _isShown; internal set => SetField(ref _isShown, value); }

    public string BatteryText { get => _battery; private set => SetField(ref _battery, value); }
    public string BatteryIcon { get => _batteryIcon; private set => SetField(ref _batteryIcon, value); }
    public string BatteryTip { get => _batteryTip; private set => SetField(ref _batteryTip, value); }
    public bool HasBattery { get => _hasBattery; private set => SetField(ref _hasBattery, value); }
    public string StatusText { get => _status; private set => SetField(ref _status, value); }
    public string StatusLevel { get => _statusLevel; private set => SetField(ref _statusLevel, value); }
    public string StatusTip { get => _statusTip; private set => SetField(ref _statusTip, value); }
    public string HideStatusText { get => _hideStatus; private set => SetField(ref _hideStatus, value); }
    public string HideLevel { get => _hideLevel; private set => SetField(ref _hideLevel, value); }
    public string HideStatusTip { get => _hideStatusTip; private set => SetField(ref _hideStatusTip, value); }
    public string ReportRateText { get => _rate; private set => SetField(ref _rate, value); }
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
        _host.SetOutputMode(Session, mode);
        _tick = 0; // refresh the status pill soon
    }

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
        if (Session.GetSummary() is { } summary)
        {
            Left.UpdateSummary(summary.Left, _snap.Left.GuidedInProgress);
            Right.UpdateSummary(summary.Right, _snap.Right.GuidedInProgress);
        }
        L2.UpdateSummary(_snap.L2);
        R2.UpdateSummary(_snap.R2);
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
        _lastRateTimestamp = now;
        _lastReports = _snap.Reports;
    }

    private void UpdateBattery(byte? percent)
    {
        HasBattery = percent.HasValue;
        SidebarMeta = percent is { } b ? $"{ModelShort} · {ConnectionText} · {b}%" : $"{ModelShort} · {ConnectionText}";
        if (percent is not { } p) return;
        BatteryText = $"{p}%";
        BatteryIcon = p >= 95 ? "" : char.ConvertFromUtf32(0xE850 + Math.Clamp(p / 10, 0, 9));
        BatteryTip = IsBluetooth
            ? $"Batterij: {p}%. " + (p <= 20 ? "Bijna leeg: sluit de kabel aan om op te laden." : "Gaat ongeveer 4 tot 8 uur mee op een volle lading.")
            : $"Batterij: {p}%. De controller laadt op via de USB-kabel.";
    }

    private void UpdateStatus()
    {
        bool stale = _snap.HasInput && (System.Diagnostics.Stopwatch.GetTimestamp() - _snap.LastReportTimestamp)
            > System.Diagnostics.Stopwatch.Frequency * 2;
        switch (Session.VirtualState)
        {
            case VirtualPadState.Active when !_snap.HasInput || stale:
                StatusText = "Wacht op invoer…";
                StatusLevel = Level.Learning;
                StatusTip = "De controller is verbonden maar stuurt (nog) geen standen. Bij Bluetooth: druk op de PS-knop. Blijft dit staan, sluit hem dan opnieuw aan.";
                break;
            case VirtualPadState.Active when Session.OutputMode == OutputMode.DualShock4:
                StatusText = "Actief als DualShock 4-controller";
                StatusLevel = Level.Ok;
                StatusTip = "Games zien deze controller nu als een (virtuele) DualShock 4, inclusief trillen. De correcties uit de kalibratie worden al toegepast. Zet 'Verberg voor games' aan zodat games niet ook je echte controller zien.";
                break;
            case VirtualPadState.Active:
                StatusText = "Actief als Xbox-controller";
                StatusLevel = Level.Ok;
                StatusTip = "Games zien deze controller nu als een Xbox 360-controller, inclusief trillen. De correcties uit de kalibratie worden al toegepast.";
                break;
            case VirtualPadState.DriverMissing:
                StatusText = "Alleen meten";
                StatusLevel = Level.Warn;
                StatusTip = "ViGEmBus ontbreekt, dus er is geen virtuele controller voor games. Je ziet hier wel alle waarden en de app leert gewoon door. Installeer ViGEmBus (zie de melding bovenaan).";
                break;
            case VirtualPadState.Failed:
                StatusText = "Virtuele controller mislukt";
                StatusLevel = Level.Bad;
                StatusTip = "De virtuele controller kon niet worden gemaakt: " + (Session.VirtualError ?? "onbekende fout") + ". Sluit de controller opnieuw aan of start de pc opnieuw op.";
                break;
            default:
                StatusText = "Starten…";
                StatusLevel = Level.Learning;
                StatusTip = "De virtuele controller wordt aangemaakt.";
                break;
        }
    }

    private void UpdateProfile()
    {
        var library = _host.Library;
        Title = library.DisplayName(Session.Id);
        if (Session.ProfileBlocked)
        {
            ProfileText = "Profiel in gebruik — draait zonder opslaan";
            ProfileLevel = Level.Warn;
            ProfileTip = Tips.ProfileBlockedOnCard;
        }
        else if (!string.Equals(Session.ProfileId, Session.Id, StringComparison.OrdinalIgnoreCase))
        {
            ProfileText = "Gebruikt profiel: " + library.DisplayName(Session.ProfileId);
            ProfileLevel = Level.Learning;
            ProfileTip = Tips.ProfileOnCard;
        }
        else
        {
            ProfileText = "";
            ProfileLevel = Level.Off;
            ProfileTip = "";
        }
    }

    private void UpdateHide()
    {
        bool enabled = _host.SettingsStore.Read(s => s.HidHideEnabled);
        if (HideAvailable != enabled)
        {
            HideAvailable = enabled;
            OnPropertyChanged(nameof(HideTip));
        }
        switch (_host.HidHide.StateFor(Session.Id))
        {
            case DeviceHideState.Hidden:
                HideStatusText = "Verborgen voor games";
                HideLevel = Level.Ok;
                HideStatusTip = "Games zien alleen de Xbox-controller, niet je echte PlayStation-controller. Een game die al draaide: herstart hem of sluit de controller opnieuw aan.";
                break;
            case DeviceHideState.Working:
                HideStatusText = "Bezig…";
                HideLevel = Level.Learning;
                HideStatusTip = "HidHide wordt ingesteld.";
                break;
            case DeviceHideState.Failed:
                HideStatusText = "Niet verborgen";
                HideLevel = Level.Warn;
                HideStatusTip = "Verbergen lukte niet, dus games kunnen deze controller twee keer zien. " +
                                (_host.HidHide.Problem ?? "Kijk bij de melding bovenaan of bij Instellingen › HidHide.");
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
                "Kalibratie resetten?",
                $"Alles wat de app over deze {Title} heeft geleerd (drift, deadzone, trigger-eindpunten) wordt vergeten. De controller begint weer met fabriekswaarden en leert daarna opnieuw.",
                "Resetten"))
            return;
        Session.ResetCalibration();
        _tick = 0; // refresh the summary on the next tick
    }
}
