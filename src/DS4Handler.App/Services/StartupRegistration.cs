using DS4Handler.App.Infrastructure;
using Microsoft.Win32;

namespace DS4Handler.App.Services;

/// <summary>"Start met Windows" through the per-user Run key (no admin rights needed).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DS4Handler";

    /// <summary>Command-line flag the Run entry passes, so an autostart can open straight into the tray.</summary>
    public const string AutostartArgument = "--autostart";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\" {AutostartArgument}");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Warn("Opstartinstelling aanpassen mislukt", ex);
        }
    }
}
