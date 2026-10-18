using System.Globalization;
using System.Text.RegularExpressions;

namespace DS4Handler.Core.Updates;

/// <summary>
/// A version like 1.2.3 or 1.2.3-beta1. Compared the semver way: numbers first, and a version with
/// a "-suffix" (pre-release or dev build) is older than the same version without one.
/// </summary>
public readonly partial record struct ReleaseVersion(int Major, int Minor, int Patch, string Suffix = "") : IComparable<ReleaseVersion>
{
    [GeneratedRegex(@"^v?(\d+)\.(\d+)(?:\.(\d+))?(?:\.\d+)?(?:-([0-9A-Za-z.-]+))?(?:\+.*)?$")]
    private static partial Regex Pattern();

    public bool IsPrerelease => Suffix.Length > 0;

    /// <summary>Parses "1.2.3", "v1.2.3", "1.2.3-beta1" and "1.2.3+commit" (build metadata is ignored).</summary>
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Pattern().Match(text.Trim());
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major)) return false;
        if (!int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor)) return false;
        int patch = 0;
        if (m.Groups[3].Success && !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
        version = new ReleaseVersion(major, minor, patch, m.Groups[4].Success ? m.Groups[4].Value : "");
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        // A version with a suffix is older than the same version without one.
        if (Suffix.Length == 0) return other.Suffix.Length == 0 ? 0 : 1;
        if (other.Suffix.Length == 0) return -1;
        return CompareSuffix(Suffix, other.Suffix);
    }

    /// <summary>Dot-separated parts; numeric parts compare as numbers ("dev.10" is newer than "dev.9").</summary>
    private static int CompareSuffix(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            bool na = long.TryParse(pa[i], NumberStyles.None, CultureInfo.InvariantCulture, out long ia);
            bool nb = long.TryParse(pb[i], NumberStyles.None, CultureInfo.InvariantCulture, out long ib);
            int c = na && nb ? ia.CompareTo(ib)
                : na ? -1
                : nb ? 1
                : string.CompareOrdinal(pa[i], pb[i]);
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }

    public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => Suffix.Length == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Suffix}";
}
