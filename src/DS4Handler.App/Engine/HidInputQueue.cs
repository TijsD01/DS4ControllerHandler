using System.Reflection;
using System.Runtime.InteropServices;
using HidSharp;

namespace DS4Handler.App.Engine;

/// <summary>
/// HidSharp asks Windows to queue 512 input reports per open handle. After a stall of the read thread
/// (a busy CPU, a GC pause) it then works through old reports one by one: up to half a second of stale
/// input on Bluetooth (1000 per second), two seconds on USB. A shorter queue caps that backlog while still
/// holding every report of a normal stall, so a short button tap is not lost.
/// </summary>
public static class HidInputQueue
{
    /// <summary>32 reports: a 32 ms stall on Bluetooth (1000/s) or 128 ms on USB (250/s) loses nothing, and draining them takes only a few ms.</summary>
    public const int Length = 32;

    /// <summary>Shortens the queue of an open stream. Best effort: returns null on success, else the reason; HidSharp's default stays.</summary>
    public static string? Shorten(HidStream stream)
    {
        try
        {
            if (HandleField(stream.GetType())?.GetValue(stream) is not IntPtr handle) return "veld _handle niet gevonden";
            if (handle == IntPtr.Zero) return "handle is gesloten";
            return HidD_SetNumInputBuffers(handle, Length) ? null : $"Win32-fout {Marshal.GetLastPInvokeError()}";
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    // shortcut: reads HidSharp 2.6's private handle of its Windows stream; a test fails if an update renames it, but not if reads stop using it: after a HidSharp upgrade check that WinHidStream.Read still uses _handle.
    public static FieldInfo? HandleField(Type streamType) =>
        streamType.GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance) is { FieldType: var t } f && t == typeof(IntPtr) ? f : null;

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetNumInputBuffers(IntPtr hidDeviceObject, int numberBuffers);
}
