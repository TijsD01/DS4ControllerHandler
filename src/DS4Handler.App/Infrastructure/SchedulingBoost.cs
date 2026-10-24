using System.Runtime;
using System.Runtime.InteropServices;

namespace DS4Handler.App.Infrastructure;

/// <summary>
/// Keeps Windows from slowing the app down while a game has focus. The app mostly sits in the tray, and
/// Windows 11 then treats it as a background app: "efficiency mode" (EcoQoS) runs its threads on slower
/// cores at lower clocks and wakes them later, which adds delay to every controller report.
/// Every step is best effort: when Windows refuses one, the app works as before.
/// </summary>
public static class SchedulingBoost
{
    /// <summary>
    /// Process-wide, once at startup: efficiency mode off and a garbage collector that avoids long
    /// pauses (the input path allocates nothing, but the UI does). The process priority stays normal on
    /// purpose: only the read threads need priority (see <see cref="EnterGamesThread"/>), and a raised
    /// process would also put the UI above the game.
    /// </summary>
    public static void ApplyToProcess()
    {
        bool ecoOff = DisablePowerThrottling();
        try { GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency; } catch { }
        Log.Info($"Snelle planning: efficiëntiemodus uit {(ecoOff ? "ja" : "nee")}.");
    }

    /// <summary>
    /// Registers the calling thread with the Windows multimedia scheduler (MMCSS) as a "Games" task, so it
    /// is woken first when a report arrives, even while a game keeps every core busy. Returns a handle for
    /// <see cref="LeaveGamesThread"/>; zero when MMCSS is not available.
    /// </summary>
    public static IntPtr EnterGamesThread()
    {
        try
        {
            uint taskIndex = 0;
            IntPtr handle = AvSetMmThreadCharacteristicsW("Games", ref taskIndex);
            if (handle == IntPtr.Zero) return IntPtr.Zero;
            // The "Games" task already runs in MMCSS's high category; normal priority within it keeps the
            // thread from spinning ahead of the UI thread that shares its (tiny) lock.
            AvSetMmThreadPriority(handle, AvrtPriorityNormal);
            return handle;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>Undoes <see cref="EnterGamesThread"/> on the same thread. Zero does nothing.</summary>
    public static void LeaveGamesThread(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        try { AvRevertMmThreadCharacteristics(handle); } catch { }
    }

    /// <summary>Opts the process out of EcoQoS (execution speed throttling). Windows 10 1709 and later.</summary>
    private static bool DisablePowerThrottling()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = ProcessPowerThrottlingCurrentVersion,
                // Controlled, with its bit clear in StateMask: "never throttle". Only this flag: the timer flag
                // exists on Windows 11 only, older builds would reject the whole call.
                ControlMask = ProcessPowerThrottlingExecutionSpeed,
                StateMask = 0,
            };
            return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state,
                (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false; // Windows older than 10 1709
        }
    }

    private const int ProcessPowerThrottling = 4; // PROCESS_INFORMATION_CLASS.ProcessPowerThrottling
    private const uint ProcessPowerThrottlingCurrentVersion = 1;
    private const uint ProcessPowerThrottlingExecutionSpeed = 0x1;
    private const int AvrtPriorityNormal = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AvSetMmThreadPriority(IntPtr handle, int priority);

    [DllImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
