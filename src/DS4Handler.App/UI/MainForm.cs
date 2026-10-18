using DS4Handler.App.Hid;

namespace DS4Handler.App.UI;

/// <summary>Main window: one panel per connected controller, refreshed at ~30 Hz.</summary>
internal sealed class MainForm : Form
{
    private readonly ControllerManager _manager;
    private readonly Panel _list = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Label _empty = new()
    {
        Text = "Geen controller gevonden. Sluit een DualShock 4 of DualSense aan via USB of Bluetooth.",
        AutoSize = true, Padding = new Padding(12),
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };

    public MainForm(ControllerManager manager)
    {
        _manager = manager;
        Text = "DS4 Handler – adaptieve kalibratie";
        Icon = SystemIcons.Application;
        ClientSize = new Size(760, 640);
        MinimumSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;

        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 64,
            Padding = new Padding(8),
            Text = BuildFooter(),
        };

        Controls.Add(_list);
        Controls.Add(footer);

        _manager.SessionsChanged += OnSessionsChanged;
        _timer.Tick += (_, _) => RefreshPanels();
        RebuildPanels();
    }

    private string BuildFooter()
    {
        var text = "Let op: de echte controller blijft ook zichtbaar voor games naast de virtuele Xbox-controller " +
                   "(dubbele invoer). Gebruik HidHide om de Sony-controller te verbergen, net als bij DS4Windows.\n" +
                   $"Profielen: {_manager.ProfileDirectory}";
        if (_manager.ViGEmError != null) text = "⚠ ViGEmBus ontbreekt – geen virtuele controller.  " + text;
        return text;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _timer.Enabled = Visible; // no UI work while hidden in the tray
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window keeps the app running in the tray, like DS4Windows.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(RebuildPanels); } catch (InvalidOperationException) { }
    }

    private void RebuildPanels()
    {
        _list.SuspendLayout();
        var old = _list.Controls.Cast<Control>().ToList();
        _list.Controls.Clear();
        foreach (var c in old)
            if (c != _empty) c.Dispose();

        var sessions = _manager.Sessions;
        if (sessions.Count == 0)
        {
            _list.Controls.Add(_empty);
        }
        else
        {
            // Docked Top: added last appears first, so add in reverse to keep the order.
            foreach (var s in sessions.Reverse()) _list.Controls.Add(new ControllerPanel(s));
        }
        _list.ResumeLayout();
    }

    private void RefreshPanels()
    {
        foreach (Control c in _list.Controls)
            if (c is ControllerPanel p) p.RefreshValues();
    }
}
