using DS4Handler.App.Hid;
using DS4Handler.App.UI;

namespace DS4Handler.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "DS4Handler.App.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("DS4 Handler draait al (zie het systeemvak).", "DS4 Handler",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) =>
            MessageBox.Show($"Onverwachte fout: {e.Exception.Message}", "DS4 Handler", MessageBoxButtons.OK, MessageBoxIcon.Error);

        var manager = new ControllerManager();
        if (manager.ViGEmError != null)
        {
            MessageBox.Show(manager.ViGEmError, "ViGEmBus niet gevonden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Reminder: games still see the physical controller too. DS4Windows hides it with HidHide
        // (https://github.com/nefarius/HidHide); this app does not integrate with HidHide.
        manager.Start();

        using var context = new TrayContext(manager);
        Application.Run(context);
        manager.Dispose(); // no-op when Exit already disposed it
    }
}
