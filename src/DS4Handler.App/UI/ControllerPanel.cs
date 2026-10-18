using System.Globalization;
using DS4Handler.App.Hid;
using DS4Handler.Core.Input;

namespace DS4Handler.App.UI;

/// <summary>Shows one controller: raw vs calibrated values and what the calibration has learned.</summary>
internal sealed class ControllerPanel : GroupBox
{
    private const int BarMax = 1000;
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private readonly ControllerSession _session;
    private readonly Label _status = new() { AutoSize = true };
    private readonly Label _info = new() { AutoSize = true, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private readonly Button _learnButton = new() { AutoSize = true };
    private readonly Row[] _rows;

    private sealed record Row(Label Name, ProgressBar RawBar, Label RawValue, ProgressBar CalBar, Label CalValue);

    public ControllerPanel(ControllerSession session)
    {
        _session = session;
        Text = $"{session.DisplayName}  ({session.Id})";
        Padding = new Padding(8);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

        layout.Controls.Add(_status, 0, 0);
        layout.SetColumnSpan(_status, 5);

        layout.Controls.Add(Header(""), 0, 1);
        layout.Controls.Add(Header("Ruw (0–255)"), 1, 1);
        layout.Controls.Add(Header(""), 2, 1);
        layout.Controls.Add(Header("Gekalibreerd"), 3, 1);
        layout.Controls.Add(Header(""), 4, 1);

        string[] names = { "Linker stick X", "Linker stick Y", "Rechter stick X", "Rechter stick Y", "L2", "R2" };
        _rows = new Row[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            var row = new Row(
                new Label { Text = names[i], AutoSize = true, Anchor = AnchorStyles.Left },
                Bar(255), ValueLabel(),
                Bar(BarMax), ValueLabel());
            _rows[i] = row;
            int r = i + 2;
            layout.Controls.Add(row.Name, 0, r);
            layout.Controls.Add(row.RawBar, 1, r);
            layout.Controls.Add(row.RawValue, 2, r);
            layout.Controls.Add(row.CalBar, 3, r);
            layout.Controls.Add(row.CalValue, 4, r);
        }

        layout.Controls.Add(_info, 0, names.Length + 2);
        layout.SetColumnSpan(_info, 5);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill };
        var reset = new Button { Text = "Kalibratie resetten", AutoSize = true };
        reset.Click += OnResetClick;
        _learnButton.Click += (_, _) => { _session.LearningEnabled = !_session.LearningEnabled; RefreshValues(); };
        buttons.Controls.Add(reset);
        buttons.Controls.Add(_learnButton);
        layout.Controls.Add(buttons, 0, names.Length + 3);
        layout.SetColumnSpan(buttons, 5);

        Controls.Add(layout);
        RefreshValues();
    }

    public ControllerSession Session => _session;

    private static Label Header(string text) => new() { Text = text, AutoSize = true, Font = new Font(DefaultFont, FontStyle.Bold) };
    private static Label ValueLabel() => new() { AutoSize = true, Anchor = AnchorStyles.Left, Text = "–" };
    private static ProgressBar Bar(int max) => new()
    {
        Minimum = 0, Maximum = max, Width = 160, Height = 14,
        Style = ProgressBarStyle.Continuous, Anchor = AnchorStyles.Left, Margin = new Padding(3, 5, 3, 5),
    };

    private void OnResetClick(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(
            FindForm(),
            "Alles wat voor deze controller is geleerd (stick-midden, deadzone, trigger-eindpunten) wordt gewist. Doorgaan?",
            "Kalibratie resetten", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
        {
            _session.ResetCalibration();
            RefreshValues();
        }
    }

    /// <summary>Called by the form's ~30 Hz timer.</summary>
    public void RefreshValues()
    {
        var snap = _session.Latest;
        var cal = _session.GetCalibrationInfo();

        string conn = _session.Connection == ConnectionType.Bluetooth ? "Bluetooth" : "USB";
        string battery = snap?.Raw.BatteryPercent is byte b ? $"  •  Batterij {b}%" : "";
        _status.Text = $"{conn}{battery}  •  {_session.VirtualStatus}" + (snap == null ? "  •  wacht op gegevens…" : "");

        if (snap != null)
        {
            var raw = snap.Raw;
            var c = snap.Calibrated;
            SetRow(0, raw.LeftX, c.LeftX, stick: true);
            SetRow(1, raw.LeftY, c.LeftY, stick: true);
            SetRow(2, raw.RightX, c.RightX, stick: true);
            SetRow(3, raw.RightY, c.RightY, stick: true);
            SetRow(4, raw.L2, c.L2, stick: false);
            SetRow(5, raw.R2, c.R2, stick: false);
        }

        _info.Text = string.Join(Environment.NewLine,
            $"Linker stick:  midden ({F(cal.LeftCenterX)}, {F(cal.LeftCenterY)})   deadzone {cal.LeftDeadzone.ToString("P1", Nl)}",
            $"Rechter stick: midden ({F(cal.RightCenterX)}, {F(cal.RightCenterY)})   deadzone {cal.RightDeadzone.ToString("P1", Nl)}",
            Trigger("L2", cal.L2Rest, cal.L2EndStop, cal.L2Learned, cal.L2Presses),
            Trigger("R2", cal.R2Rest, cal.R2EndStop, cal.R2Learned, cal.R2Presses));

        _learnButton.Text = cal.LearningEnabled ? "Leren: aan (klik om uit te zetten)" : "Leren: uit (klik om aan te zetten)";
    }

    private static string Trigger(string name, float rest, float end, bool learned, int presses)
    {
        string state = learned ? "geleerd" : "nog aan het leren";
        return $"{name}: rust {F(rest)}  eindpunt {F(end)} ({state}, {presses} keer ingedrukt)";
    }

    private static string F(float v, string format = "0.0") => v.ToString(format, Nl);

    private void SetRow(int index, byte raw, float calibrated, bool stick)
    {
        var row = _rows[index];
        row.RawBar.Value = raw;
        row.RawValue.Text = raw.ToString(Nl);
        float fraction = stick ? (calibrated + 1f) / 2f : calibrated;
        row.CalBar.Value = (int)Math.Clamp(MathF.Round(fraction * BarMax), 0, BarMax);
        row.CalValue.Text = stick ? calibrated.ToString("+0.00;-0.00;0.00", Nl) : calibrated.ToString("P0", Nl);
    }
}
