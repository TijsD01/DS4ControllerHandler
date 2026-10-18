using DS4Handler.App.Hid;

namespace DS4Handler.App.UI;

/// <summary>Runs the app from the notification area, like DS4Windows.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly ControllerManager _manager;
    private readonly NotifyIcon _tray;
    private readonly MainForm _form;
    private readonly ToolStripMenuItem _learningItem;

    public TrayContext(ControllerManager manager)
    {
        _manager = manager;
        _form = new MainForm(manager);

        _learningItem = new ToolStripMenuItem("Leren aan") { Checked = manager.LearningEnabled, CheckOnClick = true };
        _learningItem.CheckedChanged += (_, _) =>
        {
            _manager.LearningEnabled = _learningItem.Checked;
            _tray!.ShowBalloonTip(2000, "DS4 Handler",
                _learningItem.Checked ? "Leren staat aan voor alle controllers." : "Leren staat uit; de huidige kalibratie blijft actief.",
                ToolTipIcon.Info);
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Tonen", null, (_, _) => ShowForm());
        menu.Items.Add(_learningItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "DS4 Handler",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowForm();

        _manager.SessionsChanged += OnSessionsChanged;
        ShowForm();
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        int count = _manager.Sessions.Count;
        // NotifyIcon.Text is limited to 63 characters.
        try { _form.BeginInvoke(() => _tray.Text = $"DS4 Handler – {count} controller(s)"); } catch (InvalidOperationException) { }
    }

    private void ShowForm()
    {
        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    private void ExitApp()
    {
        _manager.SessionsChanged -= OnSessionsChanged;
        _tray.Visible = false;
        _manager.Dispose(); // saves all profiles and unplugs the virtual controllers
        _form.Dispose();
        _tray.Dispose();
        ExitThread();
    }
}
