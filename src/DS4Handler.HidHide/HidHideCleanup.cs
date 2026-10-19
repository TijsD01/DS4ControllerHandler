namespace DS4Handler.HidHide;

/// <summary>What <see cref="HidHideCleanup.Plan"/> decided (and <see cref="HidHideClient.CleanUp"/> then did).</summary>
public sealed class HidHideCleanupResult
{
    /// <summary>The hide list after the cleanup.</summary>
    public required IReadOnlyList<string> Blacklist { get; init; }

    /// <summary>The allow list after the cleanup.</summary>
    public required IReadOnlyList<string> Whitelist { get; init; }

    /// <summary>Owned ids that were on the hide list and are removed from it.</summary>
    public required IReadOnlyList<string> Unhidden { get; init; }

    /// <summary>Owned ids that were not on the hide list any more (nothing to do; the caller may forget them).</summary>
    public required IReadOnlyList<string> AlreadyVisible { get; init; }

    public bool BlacklistChanged => Unhidden.Count > 0;
    public bool WhitelistChanged { get; init; }

    /// <summary>The global switch is turned off: the app turned it on and nothing is hidden any more.</summary>
    public bool Deactivate { get; init; }

    /// <summary>Devices still hidden after the cleanup (hidden by the user or another program).</summary>
    public int OthersStillHidden => Blacklist.Count;
}

/// <summary>Pure decision logic for undoing everything this app changed in HidHide (used when uninstalling).</summary>
public static class HidHideCleanup
{
    /// <param name="blacklist">Current hide list.</param>
    /// <param name="whitelist">Current allow list (a deny list when <paramref name="whitelistInverted"/>).</param>
    /// <param name="active">Current state of the global switch.</param>
    /// <param name="whitelistInverted">The allow list is inverted; it is then left alone (the app never added itself to it).</param>
    /// <param name="ownedIds">Instance ids this app hid.</param>
    /// <param name="exeNtPath">This app's exe as NT path, to remove from the allow list; null to leave the list alone.</param>
    /// <param name="activatedByApp">The app turned the global switch on.</param>
    public static HidHideCleanupResult Plan(
        IEnumerable<string> blacklist, IEnumerable<string> whitelist, bool active, bool whitelistInverted,
        IEnumerable<string> ownedIds, string? exeNtPath, bool activatedByApp)
    {
        var black = blacklist.ToList();
        var white = whitelist.ToList();
        var unhidden = new List<string>();
        var alreadyVisible = new List<string>();
        foreach (var id in ownedIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            (MultiString.RemoveAll(black, id) ? unhidden : alreadyVisible).Add(id);

        bool whiteChanged = !whitelistInverted && !string.IsNullOrWhiteSpace(exeNtPath) && MultiString.RemoveAll(white, exeNtPath);

        return new HidHideCleanupResult
        {
            Blacklist = black,
            Whitelist = white,
            Unhidden = unhidden,
            AlreadyVisible = alreadyVisible,
            WhitelistChanged = whiteChanged,
            // Only undo our own switch, and never while other devices still rely on it.
            Deactivate = activatedByApp && active && black.Count == 0,
        };
    }
}
