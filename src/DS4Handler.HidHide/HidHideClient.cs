using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DS4Handler.HidHide;

/// <summary>Thrown when the HidHide driver is missing or refuses the request.</summary>
public sealed class HidHideException : Exception
{
    public HidHideException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Talks to the HidHide driver control device <c>\\.\HidHide</c> (Windows only).
/// Reading needs the driver to be installed; changing lists / the active flag normally requires
/// administrator rights (the control device is opened read/write). When the driver is absent,
/// <see cref="IsInstalled"/> returns false and other members throw <see cref="HidHideException"/>.
/// </summary>
public sealed class HidHideClient
{
    // CTL_CODE(0x8001, 2048 + n, METHOD_BUFFERED, FILE_READ_DATA) as in HidHide's HidHide.h /
    // Nefarius.Drivers.HidHide. Device type 0x8001, access 1 (read), function 0x800.. => 0x80016000 + 4*n.
    internal const uint IOCTL_GET_WHITELIST = 0x80016000;
    internal const uint IOCTL_SET_WHITELIST = 0x80016004;
    internal const uint IOCTL_GET_BLACKLIST = 0x80016008;
    internal const uint IOCTL_SET_BLACKLIST = 0x8001600C;
    internal const uint IOCTL_GET_ACTIVE = 0x80016010;
    internal const uint IOCTL_SET_ACTIVE = 0x80016014;

    private const string DevicePath = @"\\.\HidHide";

    /// <summary>True when the HidHide control device can be opened (driver installed and accessible).</summary>
    public static bool IsInstalled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { using var h = Open(); return true; }
        catch (HidHideException) { return false; }
    }

    public static string? InstanceIdFromHidPath(string devicePath) => MultiString.InstanceIdFromHidPath(devicePath);

    public bool IsActive
    {
        get
        {
            var b = Ioctl(IOCTL_GET_ACTIVE, null, 1);
            return b.Length > 0 && b[0] != 0;
        }
    }

    public void SetActive(bool active) => Ioctl(IOCTL_SET_ACTIVE, new[] { (byte)(active ? 1 : 0) }, 0);

    public IReadOnlyList<string> GetWhitelist() => MultiString.Decode(Ioctl(IOCTL_GET_WHITELIST, null, 0, true));
    public IReadOnlyList<string> GetBlacklist() => MultiString.Decode(Ioctl(IOCTL_GET_BLACKLIST, null, 0, true));
    public void SetWhitelist(IEnumerable<string> paths) => Ioctl(IOCTL_SET_WHITELIST, MultiString.Encode(paths), 0);
    public void SetBlacklist(IEnumerable<string> ids) => Ioctl(IOCTL_SET_BLACKLIST, MultiString.Encode(ids), 0);

    /// <summary>Whitelists an application (DOS path such as C:\x\y.exe). Returns true if the list changed.</summary>
    public bool EnsureApplicationWhitelisted(string exePath)
    {
        var nt = DosPathToNtPath(exePath)
            ?? throw new HidHideException($"Could not convert '{exePath}' to an NT device path.");
        var list = GetWhitelist().ToList();
        if (!MultiString.AddIfMissing(list, nt)) return false;
        SetWhitelist(list);
        return true;
    }

    /// <summary>Adds the device instance id to the blacklist (idempotent). Returns true if changed.</summary>
    public bool HideDevice(string instanceId)
    {
        var list = GetBlacklist().ToList();
        if (!MultiString.AddIfMissing(list, instanceId)) return false;
        SetBlacklist(list);
        return true;
    }

    /// <summary>Removes the device instance id from the blacklist (idempotent). Returns true if changed.</summary>
    public bool UnhideDevice(string instanceId)
    {
        var list = GetBlacklist().ToList();
        if (!MultiString.RemoveAll(list, instanceId)) return false;
        SetBlacklist(list);
        return true;
    }

    /// <summary>Converts C:\dir\app.exe to \Device\HarddiskVolumeN\dir\app.exe using QueryDosDevice.</summary>
    public static string? DosPathToNtPath(string dosPath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (dosPath.Length < 3 || dosPath[1] != ':') return null;
        var sb = new StringBuilder(1024);
        if (QueryDosDevice(dosPath[..2], sb, sb.Capacity) == 0) return null;
        var dev = sb.ToString().Split('\0')[0];
        return dev.Length == 0 ? null : dev + dosPath[2..];
    }

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle OpenCore()
    {
        var h = CreateFile(DevicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            h.Dispose();
            string why = err switch
            {
                2 or 3 => "HidHide driver is not installed (control device not found).",
                5 => "Access to the HidHide driver was denied; run as administrator.",
                _ => new Win32Exception(err).Message
            };
            throw new HidHideException(why, new Win32Exception(err));
        }
        return h;
    }

    private static SafeFileHandle Open()
    {
        if (!OperatingSystem.IsWindows())
            throw new HidHideException("HidHide is only available on Windows.");
        return OpenCore();
    }

    /// <summary>
    /// Issues an IOCTL. For list reads (<paramref name="growing"/>) the output buffer grows until the driver's
    /// reported size fits.
    /// </summary>
    private static byte[] Ioctl(uint code, byte[]? input, int outSize, bool growing = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new HidHideException("HidHide is only available on Windows.");
        using var h = OpenCore();
        int size = growing ? 512 : outSize;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var output = new byte[size];
            bool ok = DeviceIoControl(h, code, input, input?.Length ?? 0, output, output.Length, out int returned, IntPtr.Zero);
            if (ok)
            {
                if (growing && returned > size) { size = returned; continue; }
                return output.AsSpan(0, Math.Min(returned, output.Length)).ToArray();
            }
            int err = Marshal.GetLastWin32Error();
            if (growing && (err == 122 || err == 234)) { size = Math.Max(returned, size * 2); continue; }
            throw new HidHideException(err == 5
                ? "Access to the HidHide driver was denied; run as administrator."
                : $"HidHide IOCTL 0x{code:X} failed: {new Win32Exception(err).Message}", new Win32Exception(err));
        }
        throw new HidHideException("HidHide list kept growing; giving up.");
    }

    private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[]? inBuf, int inSize,
        byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int QueryDosDevice(string name, StringBuilder target, int max);
}
