using System.ComponentModel;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

static class XInput
{
    public static string Describe()
    {
        var controllers = new List<string>();
        for (uint i = 0; i < 4; i++)
        {
            if (XInputGetState(i, out var state) != 0) continue;
            var g = state.Gamepad;
            controllers.Add(FormattableString.Invariant($"XInput[{i}] LX: {Normalize(g.LX),8:F5} LY: {Normalize(g.LY),8:F5} RX: {Normalize(g.RX),8:F5} RY: {Normalize(g.RY),8:F5}  Raw: {g.LX},{g.LY},{g.RX},{g.RY}  Buttons: 0x{g.Buttons:X4} LT: {g.LeftTrigger} RT: {g.RightTrigger} Packet: {state.Packet}"));
        }
        return controllers.Count == 0 ? "XInput: none detected" : string.Join(" | ", controllers);
    }

    public static double Normalize(short raw) => raw / (raw < 0 ? 32768.0 : 32767.0);
    public static bool TryRead(uint index, out State state) => XInputGetState(index, out state) == 0;

    // XInput does not expose a device name or distinguish Steam's virtual pads from physical pads.
    [StructLayout(LayoutKind.Sequential)]
    public struct GamepadState
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct State { public uint Packet; public GamepadState Gamepad; }
    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    static extern uint XInputGetState(uint index, out State state);
}




