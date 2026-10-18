namespace DS4Handler.Core.Output;

/// <summary>Face/shoulder/menu buttons; values equal Nefarius.ViGEm.Client DualShock4Button.Value.</summary>
[Flags]
public enum Ds4OutputButtons : ushort
{
    None = 0,
    Square = 16,
    Cross = 32,
    Circle = 64,
    Triangle = 128,
    ShoulderLeft = 256,
    ShoulderRight = 512,
    TriggerLeft = 1024,
    TriggerRight = 2048,
    Share = 4096,
    Options = 8192,
    ThumbLeft = 16384,
    ThumbRight = 32768,
}

/// <summary>Hat direction; values equal DualShock4DPadDirection.Value (None = 8).</summary>
public enum Ds4DPad : byte
{
    North = 0,
    NorthEast = 1,
    East = 2,
    SouthEast = 3,
    South = 4,
    SouthWest = 5,
    West = 6,
    NorthWest = 7,
    None = 8,
}

/// <summary>PS and touchpad click; values equal DualShock4SpecialButton.Value.</summary>
[Flags]
public enum Ds4SpecialButtons : byte
{
    None = 0,
    Ps = 1,
    Touchpad = 2,
}

/// <summary>One frame of virtual DualShock 4 output. Sticks: 0..255, 128 centered, Y down is larger.</summary>
public readonly record struct Ds4OutputReport(
    byte LeftX,
    byte LeftY,
    byte RightX,
    byte RightY,
    byte L2,
    byte R2,
    Ds4OutputButtons Buttons,
    Ds4DPad DPad,
    Ds4SpecialButtons Special);
