using DS4Handler.Core.Calibration;
using DS4Handler.Core.Input;

namespace DS4Handler.App.Engine;

/// <summary>Live calibration values of one stick, as the UI shows them.</summary>
public struct StickLive
{
    /// <summary>Learned rest position, -1..1 (up positive).</summary>
    public float CenterX, CenterY;

    /// <summary>Current round deadzone radius (0..1).</summary>
    public float Deadzone;

    /// <summary>The guided "let go of the stick" re-centre is running.</summary>
    public bool GuidedInProgress;
}

/// <summary>Live calibration values of one trigger, as the UI shows them.</summary>
public struct TriggerLive
{
    /// <summary>Learned rest value and end stop in raw counts (0..255).</summary>
    public float Rest, EndStop;

    public bool EndStopLearned;

    /// <summary>How the end stop was found (automatically, or with the guided assistant).</summary>
    public PlateauKind EndStopKind;

    public int PressCount;

    /// <summary>The guided "press fully N times" assistant is running.</summary>
    public bool GuidedInProgress;
    public int GuidedPressesRemaining;
}

/// <summary>
/// Everything the UI needs about one controller at a point in time. A plain struct the UI copies
/// with <see cref="ControllerSession.CopySnapshot"/>, so polling never allocates.
/// </summary>
public struct ControllerSnapshot
{
    public bool HasInput;
    public RawControllerState Raw;
    public CalibratedState Output;
    public ConnectionType Connection;

    /// <summary>Number of input reports handled since the session started.</summary>
    public long Reports;

    /// <summary><see cref="System.Diagnostics.Stopwatch"/> timestamp of the latest report.</summary>
    public long LastReportTimestamp;

    public bool LearningEnabled;
    public StickLive Left, Right;
    public TriggerLive L2, R2;
}
