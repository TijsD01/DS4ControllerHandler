using DS4Handler.Core.Input;

namespace DS4Handler.Core.Mapping;

/// <summary>Applies a <see cref="ButtonRemap"/> to calibrated input. Allocation-free.</summary>
public static class ButtonRemapper
{
    public const float TriggerPressThreshold = 0.5f;

    // heldKeySources: bit (int)source set when that source is pressed and mapped to a key.
    public static CalibratedState Apply(in CalibratedState s, ButtonRemap remap, out uint heldKeySources)
    {
        heldKeySources = 0;
        if (remap.IsIdentity) return s;

        Buttons buttons = Buttons.None;
        float l2 = 0f, r2 = 0f;
        for (int i = 0; i < ButtonRemap.Count; i++)
        {
            var source = (PadControl)i;
            var target = remap[source];
            if (target.Kind == RemapKind.None) continue;

            // Triggers are sources by their analog value; the input L2/R2 digital bits are ignored.
            float value;
            if (source == PadControl.L2) value = s.L2;
            else if (source == PadControl.R2) value = s.R2;
            else value = (s.Buttons & ButtonRemap.ToButtons(source)) != 0 ? 1f : 0f;
            bool pressed = value >= TriggerPressThreshold;

            if (target.Kind == RemapKind.Key)
            {
                if (pressed) heldKeySources |= 1u << i;
                continue;
            }

            var output = target.Kind == RemapKind.Same ? source : target.Output;
            if (output == PadControl.L2) l2 = Math.Max(l2, value);
            else if (output == PadControl.R2) r2 = Math.Max(r2, value);
            else if (pressed) buttons |= ButtonRemap.ToButtons(output);
        }

        if (l2 > 0f) buttons |= Buttons.L2Digital;
        if (r2 > 0f) buttons |= Buttons.R2Digital;

        return remap.SwapSticks
            ? new CalibratedState(s.RightX, s.RightY, s.LeftX, s.LeftY, l2, r2, buttons)
            : new CalibratedState(s.LeftX, s.LeftY, s.RightX, s.RightY, l2, r2, buttons);
    }
}
