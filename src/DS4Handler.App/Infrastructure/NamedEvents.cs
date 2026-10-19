using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DS4Handler.App.Infrastructure;

/// <summary>Named Windows events that a normal (non-administrator) process may set, even when this app runs as administrator.</summary>
public static class NamedEvents
{
    // SYSTEM and Administrators: full access. Interactive users: set and wait only (0x100002 =
    // SYNCHRONIZE | EVENT_MODIFY_STATE). Medium integrity label, so a normal process may set it even when
    // this one is elevated (the default label would block that).
    private const string Sddl = "D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x100002;;;IU)S:(ML;;NW;;;ME)";

    /// <summary>Creates (or opens) an auto-reset event; null when that fails.</summary>
    public static WaitHandle? TryCreateSignalableByUser(string name)
    {
        IntPtr sd = IntPtr.Zero;
        try
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(Sddl, 1, out sd, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
            var handle = CreateEventW(ref sa, false, false, name);
            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new System.ComponentModel.Win32Exception(err);
            }
            return new NativeWaitHandle(handle);
        }
        catch (Exception ex)
        {
            Log.Warn($"Gebeurtenis {name} aanmaken mislukt", ex);
            return null;
        }
        finally
        {
            if (sd != IntPtr.Zero) LocalFree(sd);
        }
    }

    private sealed class NativeWaitHandle : WaitHandle
    {
        public NativeWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr sd, IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle CreateEventW(ref SECURITY_ATTRIBUTES sa, bool manualReset, bool initialState, string name);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);
}
