using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DS4Handler.HidHide;

public enum HidHideStatus { NotInstalled, Busy, Ready }
public enum HidHideErrorKind { NotInstalled, Busy, Other }
public enum WhitelistResult { Added, AlreadyPresent, SkippedInvertedMode, RefusedSharedHost, PathConversionFailed }

public sealed class HidHideException : Exception
{
    public HidHideErrorKind Kind { get; }
    public HidHideException(HidHideErrorKind kind, string message, Exception? inner = null) : base(message, inner) => Kind = kind;
}

/// <summary>
/// Talks to the HidHide driver control device <c>\\.\HidHide</c> (Windows only).
/// The control device is exclusive (one handle at a time) and world read/write: administrator rights are NOT
/// needed. Access denied / sharing violation means another program (HidHide configuration client, DS4Windows)
/// has it open (<see cref="HidHideStatus.Busy"/>). Every operation opens the device, does all its work on that
/// single handle (the exclusive handle acts as a lock) and closes it.
/// Note: hiding only blocks NEW opens of the device; running games must reopen the pad.
/// </summary>
public sealed class HidHideClient
{
    // CTL_CODE(0x8001, 0x800+n, METHOD_BUFFERED, FILE_READ_DATA) = 0x80016000 + 4*n.
    internal const uint IOCTL_GET_WHITELIST = 0x80016000;
    internal const uint IOCTL_SET_WHITELIST = 0x80016004;
    internal const uint IOCTL_GET_BLACKLIST = 0x80016008;
    internal const uint IOCTL_SET_BLACKLIST = 0x8001600C;
    internal const uint IOCTL_GET_ACTIVE = 0x80016010;
    internal const uint IOCTL_SET_ACTIVE = 0x80016014;
    internal const uint IOCTL_GET_WLINVERSE = 0x80016018;

    private const string DevicePath = @"\\.\HidHide";
    private static readonly Guid InterfaceGuid = new("0C320FF7-BD9B-42B6-BDAF-49FEB9C91649");
    internal const string BusyMessage = "Sluit de HidHide-configuratie of een ander programma dat HidHide gebruikt.";

    /// <summary>Not installed / busy (another program holds the exclusive handle) / ready.</summary>
    public static HidHideStatus GetStatus()
    {
        if (!OperatingSystem.IsWindows()) return HidHideStatus.NotInstalled;
        try { using var h = Open(); return HidHideStatus.Ready; }
        catch (HidHideException e) when (e.Kind == HidHideErrorKind.Busy) { return HidHideStatus.Busy; }
        catch (HidHideException) { return InterfaceRegistered() ? HidHideStatus.Busy : HidHideStatus.NotInstalled; }
    }

    /// <summary>True when the driver is installed (it may still be busy).</summary>
    public static bool IsInstalled() => GetStatus() != HidHideStatus.NotInstalled;

    public static string? InstanceIdFromHidPath(string devicePath) => MultiString.InstanceIdFromHidPath(devicePath);

    public bool IsActive { get { using var h = Open(); return GetFlag(h, IOCTL_GET_ACTIVE); } }

    /// <summary>True when the whitelist is inverted (listed apps are hidden from instead of allowed).</summary>
    public bool IsWhitelistInverted { get { using var h = Open(); return GetFlag(h, IOCTL_GET_WLINVERSE); } }

    public void SetActive(bool active)
    {
        using var h = Open();
        Ioctl(h, IOCTL_SET_ACTIVE, new[] { (byte)(active ? 1 : 0) }, 0);
    }

    public IReadOnlyList<string> GetWhitelist() { using var h = Open(); return ReadList(h, IOCTL_GET_WHITELIST); }
    public IReadOnlyList<string> GetBlacklist() { using var h = Open(); return ReadList(h, IOCTL_GET_BLACKLIST); }
    public void SetWhitelist(IEnumerable<string> paths) { using var h = Open(); Ioctl(h, IOCTL_SET_WHITELIST, MultiString.Encode(paths), 0); }
    public void SetBlacklist(IEnumerable<string> ids) { using var h = Open(); Ioctl(h, IOCTL_SET_BLACKLIST, MultiString.Encode(ids), 0); }

    /// <summary>
    /// Whitelists an application (DOS path) so it still sees hidden devices. Refuses shared hosts such as
    /// dotnet.exe. In inverted mode the path is ensured absent instead and <see cref="WhitelistResult.SkippedInvertedMode"/> returned.
    /// </summary>
    public WhitelistResult EnsureApplicationWhitelisted(string exePath)
    {
        if (IsSharedHostExecutable(exePath)) return WhitelistResult.RefusedSharedHost;
        var nt = DosPathToNtPath(exePath);
        if (nt == null) return WhitelistResult.PathConversionFailed;
        using var h = Open();
        var list = ReadList(h, IOCTL_GET_WHITELIST).ToList();
        if (GetFlag(h, IOCTL_GET_WLINVERSE))
        {
            if (MultiString.RemoveAll(list, nt)) Ioctl(h, IOCTL_SET_WHITELIST, MultiString.Encode(list), 0);
            return WhitelistResult.SkippedInvertedMode;
        }
        if (!MultiString.AddIfMissing(list, nt)) return WhitelistResult.AlreadyPresent;
        Ioctl(h, IOCTL_SET_WHITELIST, MultiString.Encode(list), 0);
        return WhitelistResult.Added;
    }

    /// <summary>Adds the instance id to the blacklist (idempotent). Returns true if changed.</summary>
    public bool HideDevice(string instanceId)
    {
        using var h = Open();
        var list = ReadList(h, IOCTL_GET_BLACKLIST).ToList();
        if (!MultiString.AddIfMissing(list, instanceId)) return false;
        Ioctl(h, IOCTL_SET_BLACKLIST, MultiString.Encode(list), 0);
        return true;
    }

    /// <summary>Removes the instance id from the blacklist (idempotent). Returns true if changed.</summary>
    public bool UnhideDevice(string instanceId)
    {
        using var h = Open();
        var list = ReadList(h, IOCTL_GET_BLACKLIST).ToList();
        if (!MultiString.RemoveAll(list, instanceId)) return false;
        Ioctl(h, IOCTL_SET_BLACKLIST, MultiString.Encode(list), 0);
        return true;
    }

    /// <summary>True for host executables (dotnet.exe) that must never be whitelisted: it would unhide pads for every .NET app.</summary>
    public static bool IsSharedHostExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = path.Replace('/', '\\');
        name = name[(name.LastIndexOf('\\') + 1)..];
        return name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Converts C:\dir\app.exe to \Device\HarddiskVolumeN\dir\app.exe. Null on bad input, non-Windows, or \??\ results.</summary>
    public static string? DosPathToNtPath(string? dosPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(dosPath)) return null;
        if (dosPath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return dosPath;
        return DosToNtCore(dosPath);
    }

    [SupportedOSPlatform("windows")]
    private static string? DosToNtCore(string dosPath)
    {
        string? result = null;
        try
        {
            using var f = CreateFile(dosPath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE | 4, IntPtr.Zero, OPEN_EXISTING, 0x02000000, IntPtr.Zero);
            if (!f.IsInvalid)
            {
                var sb = new StringBuilder(1024);
                int n = GetFinalPathNameByHandleW(f, sb, sb.Capacity, 0x2 /* VOLUME_NAME_NT | FILE_NAME_NORMALIZED */);
                if (n > 0 && n < sb.Capacity) result = sb.ToString();
            }
        }
        catch (Exception) { }
        if (string.IsNullOrEmpty(result) && dosPath.Length >= 3 && dosPath[1] == ':')
        {
            var sb = new StringBuilder(1024);
            if (QueryDosDevice(dosPath[..2], sb, sb.Capacity) != 0)
            {
                var dev = sb.ToString().Split('\0')[0];
                if (dev.Length > 0) result = dev + dosPath[2..];
            }
        }
        if (string.IsNullOrEmpty(result) || result.StartsWith(@"\??\", StringComparison.Ordinal)
            || !result.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return null;
        return result;
    }

    private static bool GetFlag(SafeFileHandle h, uint code)
    {
        var b = Ioctl(h, code, null, 1);
        return b.Length > 0 && b[0] != 0;
    }

    private static List<string> ReadList(SafeFileHandle h, uint code) => MultiString.Decode(Ioctl(h, code, null, 0, true));

    private static SafeFileHandle Open()
    {
        if (!OperatingSystem.IsWindows())
            throw new HidHideException(HidHideErrorKind.NotInstalled, "HidHide is alleen beschikbaar op Windows.");
        return OpenCore();
    }

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle OpenCore()
    {
        int err = 0;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var h = CreateFile(DevicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
            if (!h.IsInvalid) return h;
            err = Marshal.GetLastWin32Error();
            h.Dispose();
            if (err != 5 && err != 32) break;
            Thread.Sleep(50 * (attempt + 1));
        }
        var inner = new Win32Exception(err);
        throw err switch
        {
            2 or 3 => new HidHideException(HidHideErrorKind.NotInstalled, "HidHide is niet geinstalleerd.", inner),
            5 or 32 => new HidHideException(HidHideErrorKind.Busy, BusyMessage, inner),
            _ => new HidHideException(HidHideErrorKind.Other, inner.Message, inner)
        };
    }

    private static byte[] Ioctl(SafeFileHandle h, uint code, byte[]? input, int outSize, bool growing = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new HidHideException(HidHideErrorKind.NotInstalled, "HidHide is alleen beschikbaar op Windows.");
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
            throw new HidHideException(HidHideErrorKind.Other,
                $"HidHide IOCTL 0x{code:X} mislukt: {new Win32Exception(err).Message}", new Win32Exception(err));
        }
        throw new HidHideException(HidHideErrorKind.Other, "HidHide-lijst bleef groeien; opgegeven.");
    }

    private static bool InterfaceRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var g = InterfaceGuid;
            return CM_Get_Device_Interface_List_SizeW(out int len, ref g, null, 0) == 0 && len > 1;
        }
        catch (Exception) { return false; }
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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder path, int max, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out int len, ref Guid g, string? deviceId, uint flags);
}
