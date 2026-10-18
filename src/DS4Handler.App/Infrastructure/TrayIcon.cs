using System.Windows.Forms;

namespace DS4Handler.App.Infrastructure;

/// <summary>Notification-area icon with Tonen / Leren / Afsluiten, like DS4Windows.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _learning;

    public TrayIcon(Action show, Action<bool> setLearning, Func<bool> isLearning, Action quit)
    {
        var showItem = new ToolStripMenuItem("Tonen", null, (_, _) => show())
        {
            ToolTipText = "Opent het venster met je controllers en hun kalibratie.",
            Font = new System.Drawing.Font(SystemFonts(), System.Drawing.FontStyle.Bold),
        };
        _learning = new ToolStripMenuItem("Leren")
        {
            CheckOnClick = true,
            ToolTipText = "Aan: de app blijft bijleren hoe je controllers zich gedragen (drift, versleten triggers). Uit: de huidige correctie blijft werken, maar verandert niet meer.",
        };
        _learning.Click += (_, _) => setLearning(_learning.Checked);
        var quitItem = new ToolStripMenuItem("Afsluiten", null, (_, _) => quit())
        {
            ToolTipText = "Sluit DS4 Handler helemaal af. De virtuele controllers verdwijnen dan.",
        };

        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        menu.Items.Add(showItem);
        menu.Items.Add(_learning);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);
        menu.Opening += (_, _) => _learning.Checked = isLearning();

        _icon = new NotifyIcon
        {
            Icon = AppIcon.CreateTrayIcon(),
            Text = "DS4 Handler",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) show(); };
    }

    /// <summary>Hover text (Windows allows at most 63 characters).</summary>
    public string Text
    {
        set
        {
            var v = value.Length > 63 ? value[..63] : value;
            if (_icon.Text != v) _icon.Text = v;
        }
    }

    public void ShowBalloon(string title, string text) => _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

    private static System.Drawing.Font SystemFonts() => System.Drawing.SystemFonts.MenuFont ?? new System.Drawing.Font("Segoe UI", 9f);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
