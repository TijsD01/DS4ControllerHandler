using System.Reflection;
using System.Runtime.InteropServices;
using HidSharp;

namespace DS4Handler.App.Engine;

/// <summary>
/// HidSharp asks Windows to queue 512 input reports per open handle. After any stall of the read thread
/// (a busy CPU, a GC pause) it then works through old reports one by one: up to half a second of stale
/// input on Bluetooth (1000 per second), two seconds on USB. A short queue keeps every read on the newest
/// report; reports that would be dropped are stale by then anyway.
/// </summary>
public static class HidInputQueue
{
    /// <summary>Room for a Bluetooth burst (several reports at once), but never more than a few ms behind.</summary>
    public const int Length = 4;

    /// <summary>Shortens the queue of an open stream. Best effort: false leaves HidSharp's default.</summary>
    public static bool Shorten(HidStream stream)
    {
        try
        {
            return HandleField(stream.GetType())?.GetValue(stream) is IntPtr handle && handle != IntPtr.Zero
                && HidD_SetNumInputBuffers(handle, Length);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or FieldAccessException)
        {
            return false;
        }
    }

    // shortcut: reads HidSharp 2.6's private handle of its Windows stream; a test fails if an update renames it.
    public static FieldInfo? HandleField(Type streamType) =>
        streamType.GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance) is { FieldType: var t } f && t == typeof(IntPtr) ? f : null;

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetNumInputBuffers(IntPtr hidDeviceObject, int numberBuffers);
}
