namespace DS4Handler.Core.Output;

/// <summary>Xbox 360 button bits, matching ViGEm's Xbox360 report layout.</summary>
[Flags]
public enum XboxButtons : ushort
{
    None = 0,
    Up = 0x0001,
    Down = 0x0002,
    Left = 0x0004,
    Right = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

/// <summary>One frame of virtual Xbox 360 controller output. Thumb Y is up-positive.</summary>
public readonly record struct XboxReport(
    short LeftThumbX,
    short LeftThumbY,
    short RightThumbX,
    short RightThumbY,
    byte LeftTrigger,
    byte RightTrigger,
    XboxButtons Buttons);
