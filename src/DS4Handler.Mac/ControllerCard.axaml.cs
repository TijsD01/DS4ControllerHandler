using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using DS4Handler.App.Engine;
using DS4Handler.Core.Input;
using DS4Handler.Mac.Controls;

namespace DS4Handler.Mac;

/// <summary>One connected controller: live sticks and triggers, what was learned, and the calibration actions.</summary>
public partial class ControllerCard : UserControl
{
    private static readonly IBrush OkDot = ThemeBrush.Get("OkBrush");
    private static readonly IBrush LearningDot = ThemeBrush.Get("WarnBrush");

    private readonly ControllerSession? _session;
    private ControllerSnapshot _snap;
    private int _tick;

    /// <summary>An empty card; <see cref="Show"/> and <see cref="ShowSummary"/> fill it.</summary>
    public ControllerCard()
    {
        InitializeComponent();
        ShowGuided(default, default, default);
    }

    public ControllerCard(ControllerSession session, MacHost host) : this()
    {
        _session = session;
        ShowIdentity(session.DisplayName, session.Connection, session.Mac, session.HasStableId);
        Learning.IsChecked = session.LearningEnabled;
        Jitter.IsChecked = session.JitterFilterEnabled;
        Learning.IsCheckedChanged += (_, _) => host.SetLearning(session, Learning.IsChecked == true);
        Jitter.IsCheckedChanged += (_, _) =>
        {
            host.SetJitterFilter(session, Jitter.IsChecked == true);
            _tick = 0; // the summary mentions the filter: refresh it on the next tick
        };
    }

    public ControllerSession? Session => _session;

    /// <summary>Called ~30 times per second. CopySnapshot is cheap; the summary allocates, so it runs twice per second.</summary>
    public void Tick()
    {
        if (_session == null) return;
        _session.CopySnapshot(ref _snap);
        Show(_snap);
        if (_tick++ % 15 == 0) ShowSummary(_snap, _session.GetSummary(), _session.ProfileBlocked);
    }

    public void ShowIdentity(string name, ConnectionType connection, string? mac, bool stableId)
    {
        Title.Text = name;
        bool bluetooth = connection == ConnectionType.Bluetooth;
        ConnectionText.Text = bluetooth ? "Bluetooth" : "USB";
        ToolTip.SetTip(ConnectionText, bluetooth
            ? "Draadloos verbonden via Bluetooth. Merk je vertraging? Gebruik dan een kabel of zet de controller dichter bij je Mac."
            : "Verbonden met een USB-kabel. Dat geeft de snelste reactie en laadt de controller ondertussen op.");
        MacText.Text = mac != null ? $"MAC {mac}" : "(geen MAC gevonden)";
        ToolTip.SetTip(MacText, stableId
            ? "Het unieke adres van deze controller. Daarop wordt zijn kalibratie bewaard, dus via USB en Bluetooth gebruikt hij hetzelfde profiel."
            : "Het adres van deze controller kon niet worden gelezen. Zijn kalibratie hangt daarom aan deze USB-poort: op een andere poort begint hij opnieuw met leren.");
        ConfirmText.Text = $"Alles wat de app over deze {name} heeft geleerd (drift, deadzone, trigger-eindpunten) wordt vergeten. " +
                           "De controller begint weer met fabriekswaarden en leert daarna opnieuw.";
    }

    /// <summary>The fast-changing part: sticks, triggers and the guided assistants.</summary>
    public void Show(in ControllerSnapshot s)
    {
        NoInput.IsVisible = !s.HasInput;
        var raw = s.Raw;
        var output = s.Output;
        ShowStick(LeftStick, LeftValue, raw.LeftX, raw.LeftY, output.LeftX, output.LeftY, s.Left);
        ShowStick(RightStick, RightValue, raw.RightX, raw.RightY, output.RightX, output.RightY, s.Right);
        ShowTrigger(L2Bar, L2Value, raw.L2, output.L2, s.L2);
        ShowTrigger(R2Bar, R2Value, raw.R2, output.R2, s.R2);
        ShowGuided(s.L2, s.R2, s.Left.GuidedInProgress || s.Right.GuidedInProgress);
    }

    /// <summary>The slow part: what was learned, in words. <paramref name="summary"/> is null until the calibration is loaded.</summary>
    public void ShowSummary(in ControllerSnapshot s, CalibrationSummary? summary, bool profileBlocked)
    {
        Blocked.IsVisible = profileBlocked;
        if (summary != null)
        {
            SetLine(LeftDot, LeftSummary, CalibrationText.Stick("Linker stick", summary.Left, s.Left.GuidedInProgress));
            SetLine(RightDot, RightSummary, CalibrationText.Stick("Rechter stick", summary.Right, s.Right.GuidedInProgress));
        }
        SetLine(L2Dot, L2Summary, CalibrationText.Trigger("L2", s.L2));
        SetLine(R2Dot, R2Summary, CalibrationText.Trigger("R2", s.R2));
    }

    private static void SetLine(Ellipse dot, TextBlock text, (string Text, bool Learning) line)
    {
        dot.Fill = line.Learning ? LearningDot : OkDot;
        text.Text = line.Text;
    }

    private static void ShowStick(StickView view, TextBlock value, byte rawX, byte rawY, float outX, float outY, in StickLive live)
    {
        view.Set((rawX - 127.5) / 127.5, -(rawY - 127.5) / 127.5, outX, outY, live.CenterX, live.CenterY, live.Deadzone, live.GuidedInProgress);
        SetText(value, $"x {CalibrationText.Signed(outX)} · y {CalibrationText.Signed(outY)}");
    }

    private static void ShowTrigger(TriggerBar bar, TextBlock value, byte raw, float output, in TriggerLive live)
    {
        bar.Set(raw / 255.0, output, live.Rest / 255.0, live.EndStop / 255.0);
        SetText(value, $"{CalibrationText.Pct(raw / 255.0)} → {CalibrationText.Pct(output)}");
    }

    // Skips the layout pass when nothing changed (this runs 30 times per second).
    private static void SetText(TextBlock block, string text)
    {
        if (block.Text != text) block.Text = text;
    }

    private void ShowGuided(in TriggerLive l2, in TriggerLive r2, bool sticks)
    {
        SetGuidedButton(CalibrateL2, l2.GuidedInProgress, "Kalibreer L2", "Annuleer L2", CalibrateTriggerTip);
        SetGuidedButton(CalibrateR2, r2.GuidedInProgress, "Kalibreer R2", "Annuleer R2", CalibrateTriggerTip);
        SetGuidedButton(Recenter, sticks, "Sticks opnieuw centreren", "Annuleer centreren", RecenterTip);

        string hint = !sticks && !l2.GuidedInProgress && !r2.GuidedInProgress ? "" : string.Join("   ·   ", new[]
        {
            sticks ? "Laat beide sticks los…" : "",
            CalibrationText.GuidedHint("L2", l2),
            CalibrationText.GuidedHint("R2", r2),
        }.Where(h => h.Length > 0));
        GuidedHint.IsVisible = hint.Length > 0;
        SetText(GuidedHint, hint);
    }

    private const string CalibrateTriggerTip =
        "De snelste manier om een trigger te repareren die niet meer tot 100% komt: druk hem een paar keer helemaal in en dat punt wordt je nieuwe 100%.\n" +
        "Automatisch leren werkt ook, maar heeft meer drukken nodig.";
    private const string RecenterTip =
        "Gebruik dit als je stick ineens veel meer drift heeft dan eerst. Laat daarna beide sticks los; het nieuwe rustpunt wordt direct gemeten.\n" +
        "Gewone drift wordt ook vanzelf geleerd, daarvoor hoef je niets te doen.";
    private const string CancelTip = "Stopt de assistent. Wat al geleerd was blijft staan.";

    private static void SetGuidedButton(Button button, bool running, string start, string cancel, string tip)
    {
        string content = running ? cancel : start;
        if (button.Content as string == content) return;
        button.Content = content;
        button.Tag = running;
        ToolTip.SetTip(button, running ? CancelTip : tip);
    }

    private static bool Running(Button b) => b.Tag is true;

    private void OnCalibrateL2(object? sender, RoutedEventArgs e) => ToggleEndStop(CalibrateL2, right: false);

    private void OnCalibrateR2(object? sender, RoutedEventArgs e) => ToggleEndStop(CalibrateR2, right: true);

    private void ToggleEndStop(Button button, bool right)
    {
        if (Running(button)) _session?.CancelGuidedEndStop(right);
        else _session?.StartGuidedEndStop(right);
        _tick = 0;
    }

    private void OnRecenter(object? sender, RoutedEventArgs e)
    {
        if (Running(Recenter)) _session?.CancelGuidedCenter();
        else _session?.StartGuidedCenter();
        _tick = 0;
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        Confirm.IsVisible = true;
        CancelReset.Focus(); // the safe choice gets the keyboard
    }

    private void OnCancelReset(object? sender, RoutedEventArgs e)
    {
        Confirm.IsVisible = false;
        Reset.Focus();
    }

    private void OnConfirmReset(object? sender, RoutedEventArgs e)
    {
        Confirm.IsVisible = false;
        Reset.Focus();
        _session?.ResetCalibration();
        _tick = 0;
    }
}
