using System.Runtime.InteropServices;

namespace DS4Handler.App.Engine;

/// <summary>Sends key presses by Set-1 scan code (0xE0xx = extended key), so games that read scan codes see them.</summary>
internal static class KeyboardOutput
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfExtendedKey = 0x0001;
    private const uint KeyeventfKeyUp = 0x0002;
    private const uint KeyeventfScanCode = 0x0008;
    private static readonly int InputSize = Marshal.SizeOf<Input>();

    public static void Press(ushort scanCode) => Send(scanCode, 0);

    public static void Release(ushort scanCode) => Send(scanCode, KeyeventfKeyUp);

    private static void Send(ushort scanCode, uint flags)
    {
        flags |= KeyeventfScanCode;
        if ((scanCode >> 8) == 0xE0) flags |= KeyeventfExtendedKey;
        var input = new Input
        {
            Type = InputKeyboard,
            Keyboard = new KeybdInput { Scan = (ushort)(scanCode & 0xFF), Flags = flags },
        };
        SendInput(1, ref input, InputSize); // best effort: a blocked input (UIPI) is not worth stopping the pad for
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public KeybdInput Keyboard;
        private readonly uint _pad1, _pad2; // the union is as large as MOUSEINPUT (KEYBDINPUT is 8 bytes smaller)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, ref Input input, int size);
}
