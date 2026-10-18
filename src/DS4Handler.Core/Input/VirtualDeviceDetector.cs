namespace DS4Handler.Core.Input;

/// <summary>
/// Recognises controllers created by ViGEmBus (our own virtual DualShock 4 shows up as a real Sony
/// device). The app walks a device's parents and passes their hardware ids here; if any of them is
/// the ViGEm bus, the device is virtual and must be ignored, or the app would pick up its own output.
/// </summary>
public static class VirtualDeviceDetector
{
    private static readonly string[] BusMarkers =
    {
        @"Nefarius\ViGEmBus",
        @"ViGEm\ViGEmBus",
        @"ROOT\ViGEmBus",
    };

    /// <summary>True when one of the hardware ids belongs to the ViGEm virtual bus.</summary>
    public static bool IsVirtualBusHardwareId(string? hardwareId)
    {
        if (string.IsNullOrEmpty(hardwareId)) return false;
        foreach (var marker in BusMarkers)
            if (hardwareId.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>True when any id in any parent's hardware id list belongs to the ViGEm bus.</summary>
    public static bool AnyVirtualBus(IEnumerable<string> hardwareIds)
    {
        foreach (var id in hardwareIds)
            if (IsVirtualBusHardwareId(id)) return true;
        return false;
    }
}
