namespace DS4Handler.Core.Input;

/// <summary>Which physical controller a report came from.</summary>
public enum ControllerModel
{
    DualShock4,
    DualSense,
}

/// <summary>How the controller is connected; reports differ per connection.</summary>
public enum ConnectionType
{
    Usb,
    Bluetooth,
}

[Flags]
public enum Buttons : uint
{
    None = 0,
    Cross = 1 << 0,
    Circle = 1 << 1,
    Square = 1 << 2,
    Triangle = 1 << 3,
    L1 = 1 << 4,
    R1 = 1 << 5,
    L2Digital = 1 << 6,
    R2Digital = 1 << 7,
    Share = 1 << 8,      // "Create" on DualSense
    Options = 1 << 9,
    L3 = 1 << 10,
    R3 = 1 << 11,
    PS = 1 << 12,
    TouchpadClick = 1 << 13,
    Mute = 1 << 14,      // DualSense only
    DpadUp = 1 << 15,
    DpadDown = 1 << 16,
    DpadLeft = 1 << 17,
    DpadRight = 1 << 18,
}

/// <summary>
/// Raw controller input exactly as the hardware reports it.
/// Sticks and triggers are 0..255; sticks rest near 128 and Y grows downward.
/// </summary>
public readonly record struct RawControllerState(
    byte LeftX,
    byte LeftY,
    byte RightX,
    byte RightY,
    byte L2,
    byte R2,
    Buttons Buttons,
    byte? BatteryPercent = null)
{
    public bool IsPressed(Buttons b) => (Buttons & b) == b;
}

/// <summary>
/// Input after calibration. Sticks are -1..1 (Y up is positive), triggers 0..1.
/// </summary>
public readonly record struct CalibratedState(
    float LeftX,
    float LeftY,
    float RightX,
    float RightY,
    float L2,
    float R2,
    Buttons Buttons);
