namespace DS4Handler.Core.Mapping;

/// <summary>
/// Turns the key-mapped sources held per report into key press / release edges. Remembers the scan
/// code each source was pressed with, so a remap change releases the right key. A scan code shared by
/// several sources is pressed once and released when the last of them lets go. Not thread-safe.
/// </summary>
public sealed class KeyEdgeTracker
{
    private readonly ushort[] _pressedWith = new ushort[ButtonRemap.Count]; // 0 = no key held

    /// <summary>Calls press/release for each change. Allocation-free.</summary>
    public void Update(uint heldKeySources, ButtonRemap remap, Action<ushort> press, Action<ushort> release)
    {
        for (int i = 0; i < ButtonRemap.Count; i++)
        {
            var target = remap[(PadControl)i];
            ushort wanted = (heldKeySources & (1u << i)) != 0 && target.Kind == RemapKind.Key ? target.ScanCode : (ushort)0;
            ushort old = _pressedWith[i];
            if (old == wanted) continue;
            _pressedWith[i] = 0;
            if (old != 0 && !IsHeld(old)) release(old);
            if (wanted != 0 && !IsHeld(wanted)) press(wanted);
            _pressedWith[i] = wanted;
        }
    }

    public void ReleaseAll(Action<ushort> release)
    {
        for (int i = 0; i < ButtonRemap.Count; i++)
        {
            ushort old = _pressedWith[i];
            if (old == 0) continue;
            _pressedWith[i] = 0;
            if (!IsHeld(old)) release(old);
        }
    }

    private bool IsHeld(ushort scanCode) => Array.IndexOf(_pressedWith, scanCode) >= 0;
}
