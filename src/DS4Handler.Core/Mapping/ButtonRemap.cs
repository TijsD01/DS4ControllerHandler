using DS4Handler.Core.Input;

namespace DS4Handler.Core.Mapping;

// Order is persisted by name, not value. Used as both source and pad target. Mute is source-only.
public enum PadControl { Cross, Circle, Square, Triangle, L1, R1, L2, R2, Share, Options, L3, R3, PS, TouchpadClick, Mute, DpadUp, DpadDown, DpadLeft, DpadRight }

public enum RemapKind { Same, Pad, Key, None }

// Pad: Output is the target. Key: ScanCode is a Set-1 scan code, 0xE0xx for extended keys.
public readonly record struct RemapTarget(RemapKind Kind, PadControl Output = default, ushort ScanCode = 0)
{
    public static RemapTarget Same => default;
    public static RemapTarget Disabled => new(RemapKind.None);
    public static RemapTarget ToPad(PadControl c) => new(RemapKind.Pad, c);
    public static RemapTarget ToKey(ushort scanCode) => new(RemapKind.Key, default, scanCode);

    /// <summary>Plain (1..0xFF) or 0xE0-extended scan code; the only shapes one key event can send.</summary>
    public static bool IsSendableScanCode(int code) => (code & 0xFF) != 0 && (code >> 8) is 0 or 0xE0;
}

/// <summary>Immutable per-source button remap plus a stick swap. Value-equal.</summary>
public sealed class ButtonRemap : IEquatable<ButtonRemap>
{
    public const int Count = 19;

    public static ButtonRemap Identity { get; } = new(new RemapTarget[Count], false);

    private readonly RemapTarget[] _targets;

    private ButtonRemap(RemapTarget[] targets, bool swapSticks)
    {
        _targets = targets;
        SwapSticks = swapSticks;
        IsIdentity = !swapSticks && Array.TrueForAll(targets, t => t.Kind == RemapKind.Same);
    }

    public bool SwapSticks { get; }
    public bool IsIdentity { get; }

    public RemapTarget this[PadControl source] => _targets[(int)source];

    public ButtonRemap With(PadControl source, RemapTarget target)
    {
        if (target.Kind == RemapKind.Pad)
        {
            if (target.Output == PadControl.Mute)
                throw new ArgumentException("Mute is not a pad output.", nameof(target));
            if (target.Output == source) target = RemapTarget.Same;
        }
        var targets = (RemapTarget[])_targets.Clone();
        targets[(int)source] = target;
        return new ButtonRemap(targets, SwapSticks);
    }

    public ButtonRemap WithSwapSticks(bool swap) => new(_targets, swap);

    public static Buttons ToButtons(PadControl c) => c switch
    {
        PadControl.Cross => Buttons.Cross,
        PadControl.Circle => Buttons.Circle,
        PadControl.Square => Buttons.Square,
        PadControl.Triangle => Buttons.Triangle,
        PadControl.L1 => Buttons.L1,
        PadControl.R1 => Buttons.R1,
        PadControl.L2 => Buttons.L2Digital,
        PadControl.R2 => Buttons.R2Digital,
        PadControl.Share => Buttons.Share,
        PadControl.Options => Buttons.Options,
        PadControl.L3 => Buttons.L3,
        PadControl.R3 => Buttons.R3,
        PadControl.PS => Buttons.PS,
        PadControl.TouchpadClick => Buttons.TouchpadClick,
        PadControl.Mute => Buttons.Mute,
        PadControl.DpadUp => Buttons.DpadUp,
        PadControl.DpadDown => Buttons.DpadDown,
        PadControl.DpadLeft => Buttons.DpadLeft,
        PadControl.DpadRight => Buttons.DpadRight,
        _ => Buttons.None,
    };

    public bool Equals(ButtonRemap? other) =>
        other is not null && SwapSticks == other.SwapSticks && _targets.AsSpan().SequenceEqual(other._targets);

    public override bool Equals(object? obj) => Equals(obj as ButtonRemap);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(SwapSticks);
        foreach (var t in _targets) h.Add(t);
        return h.ToHashCode();
    }
}
