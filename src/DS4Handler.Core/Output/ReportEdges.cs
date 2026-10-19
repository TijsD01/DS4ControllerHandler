namespace DS4Handler.Core.Output;

/// <summary>
/// Digital changes between two virtual-pad reports: a button, d-pad or special button, or a trigger
/// leaving / reaching 0. The output rate limit never holds these back, so a tap shorter than one rate
/// window still reaches the game (press and release each go out at once). Allocation free.
/// </summary>
public static class ReportEdges
{
    public static bool HasDigitalEdge(in XboxReport previous, in XboxReport next) =>
        previous.Buttons != next.Buttons
        || (previous.LeftTrigger == 0) != (next.LeftTrigger == 0)
        || (previous.RightTrigger == 0) != (next.RightTrigger == 0);

    /// <summary>The DS4 digital L2/R2 bits live in <see cref="Ds4OutputReport.Buttons"/>.</summary>
    public static bool HasDigitalEdge(in Ds4OutputReport previous, in Ds4OutputReport next) =>
        previous.Buttons != next.Buttons
        || previous.DPad != next.DPad
        || previous.Special != next.Special
        || (previous.L2 == 0) != (next.L2 == 0)
        || (previous.R2 == 0) != (next.R2 == 0);
}
