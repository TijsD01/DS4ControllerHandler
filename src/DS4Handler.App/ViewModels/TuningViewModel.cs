using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using DS4Handler.App.Engine;
using DS4Handler.App.Services;
using DS4Handler.Core.Tuning;

namespace DS4Handler.App.ViewModels;

/// <summary>One option in a choice list (poll rate, output rate).</summary>
public sealed record TuningChoice(int Value, string Label);

/// <summary>
/// One slider row. The slider itself always runs 0..1 (<see cref="Position"/>); the real value
/// (<see cref="Value"/>, in natural units) and its text are derived from that, so XAML needs no limits.
/// </summary>
public sealed class TuningSlider : ObservableObject
{
    private readonly double _min, _step;
    private double _max;
    private readonly Func<double, string> _format;
    private readonly Action _changed;
    private double _value, _position;

    public TuningSlider(string label, string tip, double min, double max, double step, Func<double, string> format, Action changed)
    {
        Label = label;
        Tip = tip;
        _min = min;
        _max = max;
        _step = step;
        _format = format;
        _changed = changed;
        _value = min;
    }

    public string Label { get; }
    public string Tip { get; }
    public string Text => _format(_value);

    /// <summary>The real value, in natural units (for example a fraction 0..0.9).</summary>
    public double Value => _value;

    /// <summary>Slider position 0..1. Only user input calls <see cref="_changed"/>.</summary>
    public double Position
    {
        get => _position;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(value - _position) < 1e-9) return;
            _position = value;
            double v = Snap(_min + value * (_max - _min));
            bool valueChanged = Math.Abs(v - _value) > 1e-9;
            _value = v;
            OnPropertyChanged();
            if (!valueChanged) return;
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(Text));
            _changed();
        }
    }

    /// <summary>Sets the value from the model without raising a change (no echo to the engine).</summary>
    public void Load(double value)
    {
        _value = Math.Clamp(value, _min, _max);
        _position = _max > _min ? (_value - _min) / (_max - _min) : 0;
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Text));
    }

    /// <summary>
    /// Changes the upper limit (the slider still runs 0..1, so the thumb moves). A value above the new
    /// limit is clamped down. Never raises a change itself: returns true when the value was clamped.
    /// </summary>
    public bool SetMax(double max)
    {
        max = Math.Max(max, _min);
        if (Math.Abs(max - _max) < 1e-9) return false;
        _max = max;
        bool clamped = _value > _max + 1e-9;
        if (clamped) _value = _max;
        _position = _max > _min ? (_value - _min) / (_max - _min) : 0;
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Text));
        return clamped;
    }

    private double Snap(double v) => Math.Clamp(Math.Round(v / _step) * _step, _min, _max);
}

/// <summary>Shared pieces of the stick and trigger sections.</summary>
public abstract class TuningSection : ObservableObject
{
    private readonly Action _changed;
    protected bool Loading;

    protected TuningSection(string title, string group, Action changed)
    {
        Title = title;
        Group = group;
        _changed = changed;
        ResetCommand = new RelayCommand(() =>
        {
            LoadDefaults();
            _changed();
        });
    }

    public string Title { get; }

    /// <summary>Unique radio group name (radio buttons in different sections must not exclude each other).</summary>
    public string Group { get; }

    public ICommand ResetCommand { get; }

    protected abstract void LoadDefaults();

    protected void Notify()
    {
        if (!Loading) _changed();
    }

    protected Action ChangedCallback => Notify;

    protected static string Pct(double v) => Fmt.Pct(v);

    protected static string Times(double v) => v.ToString("0.00", Fmt.Nl) + "×";

    protected static string CurveText(double v) => v.ToString("0.00", Fmt.Nl);

    /// <summary>Caption under the curve preview: "curve 1,40 · zachter in het midden".</summary>
    public string CurveCaption
    {
        get
        {
            double v = CurveValue;
            string how = Math.Abs(v - 1) < 0.025 ? "recht" : v > 1 ? "zachter in het midden" : "sneller uit het midden";
            return $"curve {CurveText(v)} · {how}";
        }
    }

    protected abstract double CurveValue { get; }

    /// <summary>Keeps <see cref="CurveCaption"/> in step with the curve slider.</summary>
    protected void WatchCurve(TuningSlider curve) =>
        curve.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TuningSlider.Text)) OnPropertyChanged(nameof(CurveCaption)); };
}

/// <summary>Settings of one stick.</summary>
public sealed class StickTuningSection : TuningSection
{
    private DeadzoneShape _shape;
    private bool _square, _invertX, _invertY;

    public StickTuningSection(string title, string group, Action changed) : base(title, group, changed)
    {
        Inner = new TuningSlider("Extra deadzone", Tips.TuneInner, 0, InputShaper.MaxStickInnerDeadzone, 0.01, Pct, ChangedCallback);
        Outer = new TuningSlider("Eindpunt", Tips.TuneOuter, 0, 0.5, 0.01, Pct, OuterChanged);
        Anti = new TuningSlider("Anti-deadzone", Tips.TuneAnti, 0, 0.9, 0.01, Pct, ChangedCallback);
        Curve = new TuningSlider("Curve", Tips.TuneCurve, 0.2, 5, 0.05, CurveText, ChangedCallback);
        Sensitivity = new TuningSlider("Gevoeligheid", Tips.TuneSensitivity, 0.1, 3, 0.05, Times, ChangedCallback);
        Sliders = new[] { Inner, Outer, Anti, Curve, Sensitivity };
        WatchCurve(Curve);
        LoadDefaults();
    }

    protected override double CurveValue => Curve.Value;

    public TuningSlider Inner { get; }
    public TuningSlider Outer { get; }
    public TuningSlider Anti { get; }
    public TuningSlider Curve { get; }
    public TuningSlider Sensitivity { get; }
    public IReadOnlyList<TuningSlider> Sliders { get; }

    public bool ShapeRadial { get => _shape == DeadzoneShape.Radial; set { if (value) SetShape(DeadzoneShape.Radial); } }
    public bool ShapeSquare { get => _shape == DeadzoneShape.Square; set { if (value) SetShape(DeadzoneShape.Square); } }
    public bool ShapeAxial { get => _shape == DeadzoneShape.Axial; set { if (value) SetShape(DeadzoneShape.Axial); } }

    public bool SquareOutput { get => _square; set { if (SetField(ref _square, value)) Notify(); } }
    public bool InvertX { get => _invertX; set { if (SetField(ref _invertX, value)) Notify(); } }
    public bool InvertY { get => _invertY; set { if (SetField(ref _invertY, value)) Notify(); } }

    private void OuterChanged()
    {
        UpdateInnerMax();
        Notify();
    }

    /// <summary>Inner deadzone limit follows shape and outer deadzone (see <see cref="InputShaper.MaxInnerDeadzone"/>).</summary>
    private void UpdateInnerMax()
    {
        double max = InputShaper.MaxInnerDeadzone(_shape, (float)Outer.Value);
        Inner.SetMax(Math.Floor(max * 100 + 1e-4) / 100);
    }

    private void SetShape(DeadzoneShape shape)
    {
        if (_shape == shape) return;
        _shape = shape;
        UpdateInnerMax();
        OnPropertyChanged(nameof(ShapeRadial));
        OnPropertyChanged(nameof(ShapeSquare));
        OnPropertyChanged(nameof(ShapeAxial));
        Notify();
    }

    protected override void LoadDefaults() => Load(new StickTuning());

    public void Load(StickTuning t)
    {
        Loading = true;
        try
        {
            _shape = t.Shape;
            OnPropertyChanged(nameof(ShapeRadial));
            OnPropertyChanged(nameof(ShapeSquare));
            OnPropertyChanged(nameof(ShapeAxial));
            Outer.Load(t.OuterDeadzone);
            UpdateInnerMax();
            Inner.Load(t.InnerDeadzone);
            Anti.Load(t.AntiDeadzone);
            Curve.Load(t.Curve);
            Sensitivity.Load(t.Sensitivity);
            SquareOutput = t.SquareOutput;
            InvertX = t.InvertX;
            InvertY = t.InvertY;
        }
        finally { Loading = false; }
    }

    public StickTuning ToModel() => new()
    {
        Shape = _shape,
        InnerDeadzone = F(Inner.Value),
        OuterDeadzone = F(Outer.Value),
        AntiDeadzone = F(Anti.Value),
        Curve = F(Curve.Value),
        Sensitivity = F(Sensitivity.Value),
        SquareOutput = _square,
        InvertX = _invertX,
        InvertY = _invertY,
    };

    private static float F(double v) => (float)Math.Round(v, 2);
}

/// <summary>Settings of one trigger.</summary>
public sealed class TriggerTuningSection : TuningSection
{
    public TriggerTuningSection(string title, string group, Action changed) : base(title, group, changed)
    {
        Inner = new TuningSlider("Extra deadzone", Tips.TuneTriggerInner, 0, InputShaper.MaxTriggerInnerDeadzone, 0.01, Pct, ChangedCallback);
        Outer = new TuningSlider("Eindpunt", Tips.TuneTriggerOuter, 0, 0.5, 0.01, Pct, OuterChanged);
        Anti = new TuningSlider("Anti-deadzone", Tips.TuneTriggerAnti, 0, 0.9, 0.01, Pct, ChangedCallback);
        Curve = new TuningSlider("Curve", Tips.TuneCurve, 0.2, 5, 0.05, CurveText, ChangedCallback);
        Sliders = new[] { Inner, Outer, Anti, Curve };
        WatchCurve(Curve);
        LoadDefaults();
    }

    protected override double CurveValue => Curve.Value;

    public TuningSlider Inner { get; }
    public TuningSlider Outer { get; }
    public TuningSlider Anti { get; }
    public TuningSlider Curve { get; }
    public IReadOnlyList<TuningSlider> Sliders { get; }

    private void OuterChanged()
    {
        UpdateInnerMax();
        Notify();
    }

    /// <summary>Inner deadzone limit follows the end point (see <see cref="InputShaper.MaxTriggerInner"/>), so Sanitize never changes what is shown.</summary>
    private void UpdateInnerMax()
    {
        double max = InputShaper.MaxTriggerInner((float)Outer.Value);
        Inner.SetMax(Math.Floor(max * 100 + 1e-4) / 100);
    }

    protected override void LoadDefaults() => Load(new TriggerTuning());

    public void Load(TriggerTuning t)
    {
        Loading = true;
        try
        {
            Outer.Load(t.OuterDeadzone);
            UpdateInnerMax();
            Inner.Load(t.InnerDeadzone);
            Anti.Load(t.AntiDeadzone);
            Curve.Load(t.Curve);
        }
        finally { Loading = false; }
    }

    public TriggerTuning ToModel() => new()
    {
        InnerDeadzone = F(Inner.Value),
        OuterDeadzone = F(Outer.Value),
        AntiDeadzone = F(Anti.Value),
        Curve = F(Curve.Value),
    };

    private static float F(double v) => (float)Math.Round(v, 2);
}

/// <summary>
/// The "Afstellen" section of one controller: every <see cref="InputTuning"/> field as bindable
/// properties. Changes are applied to the session at most every 100 ms while a slider is dragged,
/// plus once for the last value. <see cref="Refresh"/> picks up tuning that changed underneath us
/// (another profile, import) without applying it back.
/// </summary>
public sealed class TuningViewModel : ObservableObject
{
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(100);

    private readonly ControllerSession _session;
    private readonly AppHost _host;
    private readonly DispatcherTimer _timer;
    private InputTuning _current;
    private int _generation;                // session.TuningGeneration the shown values belong to
    private bool _loading, _pending;
    private TuningChoice _poll;
    private TuningChoice _outputRate;
    private bool _supportsPoll, _canSave = true;

    public static IReadOnlyList<TuningChoice> PollChoices { get; } = new TuningChoice[]
    {
        new(1, "1000 per seconde"), new(2, "500 per seconde"), new(4, "250 per seconde"),
        new(8, "125 per seconde"), new(16, "62 per seconde"),
    };

    public static IReadOnlyList<TuningChoice> OutputChoices { get; } = new TuningChoice[]
    {
        new(0, "Elke meting (standaard)"), new(1000, "1000 per seconde"), new(500, "500 per seconde"),
        new(250, "250 per seconde"), new(125, "125 per seconde"),
    };

    public TuningViewModel(ControllerSession session, AppHost host)
    {
        _session = session;
        _host = host;
        _timer = new DispatcherTimer { Interval = Throttle };
        _timer.Tick += (_, _) => { if (!Flush()) _timer.Stop(); };

        Left = new StickTuningSection("Linker stick", "tune-left-" + session.Id, Changed);
        Right = new StickTuningSection("Rechter stick", "tune-right-" + session.Id, Changed);
        L2 = new TriggerTuningSection("L2", "tune-l2-" + session.Id, Changed);
        R2 = new TriggerTuningSection("R2", "tune-r2-" + session.Id, Changed);
        ResetAllCommand = new RelayCommand(() =>
        {
            Load(InputTuning.Default with { BluetoothPollIntervalMs = StoredPoll });
            Changed();
        });
        ResetSpeedCommand = new RelayCommand(() =>
        {
            SetSpeed(InputTuning.Default with { BluetoothPollIntervalMs = StoredPoll });
            Changed();
        });

        _generation = session.TuningGeneration; // before Tuning, see ControllerSession.TuningGeneration
        _current = session.Tuning;
        _poll = PollChoices[0];
        _outputRate = OutputChoices[0];
        Load(_current);
        UpdateFlags();
    }

    public StickTuningSection Left { get; }
    public StickTuningSection Right { get; }
    public TriggerTuningSection L2 { get; }
    public TriggerTuningSection R2 { get; }
    public ICommand ResetAllCommand { get; }

    private string _section = SectionSticks;
    private const string SectionSticks = "sticks", SectionTriggers = "triggers", SectionSpeed = "speed";

    // View state only (not saved): which part of Afstellen is shown. The segmented control binds to these;
    // unchecking (false) is ignored, checking switches.
    public bool ShowSticks { get => _section == SectionSticks; set { if (value) SetSection(SectionSticks); } }
    public bool ShowTriggers { get => _section == SectionTriggers; set { if (value) SetSection(SectionTriggers); } }
    public bool ShowSpeed { get => _section == SectionSpeed; set { if (value) SetSection(SectionSpeed); } }

    private void SetSection(string section)
    {
        if (_section == section) return;
        _section = section;
        OnPropertyChanged(nameof(ShowSticks));
        OnPropertyChanged(nameof(ShowTriggers));
        OnPropertyChanged(nameof(ShowSpeed));
    }
    public ICommand ResetSpeedCommand { get; }

    public bool SupportsPoll { get => _supportsPoll; private set { if (SetField(ref _supportsPoll, value)) OnPropertyChanged(nameof(PollNote)); } }

    /// <summary>Shown next to the poll rate when the controller cannot change it.</summary>
    public string PollNote => SupportsPoll ? "" : "Alleen te kiezen voor een DualShock 4 via Bluetooth. Via USB bepaalt de controller zelf hoe vaak hij meet.";

    private bool CanSave { get => _canSave; set { if (SetField(ref _canSave, value)) OnPropertyChanged(nameof(SaveNote)); } }

    public string SaveNote => CanSave ? ""
        : _blocked ? "Wijzigingen werken nu, maar worden niet bewaard: dit profiel is in gebruik door een andere controller."
        : "Wijzigingen werken nu, maar worden niet bewaard: het profielbestand kon niet worden gelezen. Het bestand blijft ongemoeid.";

    private bool _blocked;

    public TuningChoice SelectedPoll
    {
        get => _poll;
        set { if (value != null && SetField(ref _poll, value)) Changed(); }
    }

    public TuningChoice SelectedOutputRate
    {
        get => _outputRate;
        set { if (value != null && SetField(ref _outputRate, value)) Changed(); }
    }

    /// <summary>Called by the controller view model about twice per second.</summary>
    public void Refresh()
    {
        UpdateFlags();
        // Another profile (link, unlink, import): its values win, even over an edit not yet applied,
        // which belonged to the old profile.
        if (_session.TuningGeneration != _generation)
        {
            ReloadFromSession();
            return;
        }
        if (_pending || _timer.IsEnabled) return; // the user is busy; do not fight them
        var live = _session.Tuning;
        if (!live.Equals(_current)) Load(live);
    }

    private void ReloadFromSession()
    {
        _pending = false;
        _timer.Stop();
        _generation = _session.TuningGeneration;
        Load(_session.Tuning);
    }

    /// <summary>Poll interval a reset must keep: the stored one when this pad cannot change it.</summary>
    private int StoredPoll => SupportsPoll ? InputTuning.Default.BluetoothPollIntervalMs : _current.BluetoothPollIntervalMs;

    private void UpdateFlags()
    {
        SupportsPoll = _session.SupportsBluetoothPollRate;
        bool blocked = _session.ProfileBlocked;
        if (blocked != _blocked)
        {
            _blocked = blocked;
            OnPropertyChanged(nameof(SaveNote));
        }
        CanSave = _session.CanSaveTuning;
    }

    private void Load(InputTuning t)
    {
        _loading = true;
        try
        {
            Left.Load(t.LeftStick);
            Right.Load(t.RightStick);
            L2.Load(t.L2);
            R2.Load(t.R2);
            SetSpeed(t);
            _current = t;
        }
        finally { _loading = false; }
    }

    private void SetSpeed(InputTuning t)
    {
        _poll = Nearest(PollChoices, t.BluetoothPollIntervalMs);
        _outputRate = Nearest(OutputChoices, t.OutputRateHz);
        OnPropertyChanged(nameof(SelectedPoll));
        OnPropertyChanged(nameof(SelectedOutputRate));
    }

    private static TuningChoice Nearest(IReadOnlyList<TuningChoice> list, int value)
    {
        var best = list[0];
        foreach (var c in list)
            if (Math.Abs(c.Value - value) < Math.Abs(best.Value - value)) best = c;
        return best;
    }

    /// <summary>Leading edge applies at once, further changes inside the interval are merged into one apply.</summary>
    private void Changed()
    {
        if (_loading) return;
        _pending = true;
        if (_timer.IsEnabled) return;
        Flush();
        _timer.Start();
    }

    private bool Flush()
    {
        if (!_pending) return false;
        _pending = false;
        var tuning = new InputTuning
        {
            LeftStick = Left.ToModel(),
            RightStick = Right.ToModel(),
            L2 = L2.ToModel(),
            R2 = R2.ToModel(),
            // Only the Bluetooth DualShock 4 can change its poll rate; otherwise keep what the profile has.
            BluetoothPollIntervalMs = SupportsPoll ? _poll.Value : _current.BluetoothPollIntervalMs,
            OutputRateHz = _outputRate.Value,
        };
        if (!_host.SetTuning(_session, tuning, _generation))
        {
            // The profile changed underneath us: show its values instead of writing ours into it.
            ReloadFromSession();
            return false;
        }
        _current = tuning;
        return true;
    }
}
