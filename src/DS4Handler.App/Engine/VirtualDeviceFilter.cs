using System.Runtime.InteropServices;
using DS4Handler.Core.Input;
using DS4Handler.HidHide;

namespace DS4Handler.App.Engine;

/// <summary>
/// Tells real controllers apart from virtual ones made by ViGEmBus. Our own virtual DualShock 4
/// appears as a Sony device too; without this check the app would open it, create another virtual
/// pad for it, and keep multiplying. Works like DS4Windows' CheckIfVirtualDevice: walk the device's
/// parents in the device tree and look for the ViGEm bus.
/// </summary>
internal static class VirtualDeviceFilter
{
    private const int CrSuccess = 0;
    private const uint CmDrpHardwareId = 0x00000002;
    private const int MaxDepth = 8;

    /// <summary>True if the HID device at this path hangs under the ViGEm bus. Never throws.</summary>
    public static bool IsVirtual(string hidDevicePath)
    {
        try
        {
            var instanceId = HidHideClient.InstanceIdFromHidPath(hidDevicePath);
            if (instanceId == null) return false;
            if (CM_Locate_DevNodeW(out uint node, instanceId, 0) != CrSuccess) return false;

            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (VirtualDeviceDetector.AnyVirtualBus(HardwareIds(node))) return true;
                if (CM_Get_Parent(out uint parent, node, 0) != CrSuccess) break;
                node = parent;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not on Windows: nothing to filter.
        }
        return false;
    }

    private static List<string> HardwareIds(uint node)
    {
        var ids = new List<string>();
        uint length = 0;
        CM_Get_DevNode_Registry_PropertyW(node, CmDrpHardwareId, out _, null, ref length, 0);
        if (length == 0 || length > 64 * 1024) return ids;
        var buffer = new byte[length];
        if (CM_Get_DevNode_Registry_PropertyW(node, CmDrpHardwareId, out _, buffer, ref length, 0) != CrSuccess) return ids;
        ids.AddRange(MultiString.Decode(buffer.AsSpan(0, (int)length)));
        return ids;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(
        uint dnDevInst, uint ulProperty, out uint pulRegDataType, byte[]? buffer, ref uint pulLength, uint ulFlags);
}
