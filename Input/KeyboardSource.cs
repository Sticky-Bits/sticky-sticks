using System.Runtime.InteropServices;
sealed class KeyboardSource : ControllerSource
{
    public static ControllerChoice Choice => new("keyboard", "Keyboard", () => new KeyboardSource());
    public override string[] Axes => ["Left X", "Left Y", "Right X", "Right Y"];
    public override ControllerFrame Read()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint process);
        bool focused = process == Environment.ProcessId;
        bool Down(Keys key) => focused && (GetAsyncKeyState((int)key) & 0x8000) != 0;
        return FromKeys(Down(Keys.Left), Down(Keys.Right), Down(Keys.Up), Down(Keys.Down), Down(Keys.R), Down(Keys.F));
    }
    internal static ControllerFrame FromKeys(bool left, bool right, bool up, bool down, bool r, bool f)
    {
        int x = (right ? 1 : 0) - (left ? 1 : 0), y = (up ? 1 : 0) - (down ? 1 : 0);
        return new([x, y, 0, 0], [x, y, 0, 0], "Keyboard · Arrow keys: movement · R: Mod X · F: Mod Y")
        { LeftTrigger = r ? 255 : 0, RightTrigger = f ? 255 : 0 };
    }
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
// Keep movement keys from also changing focused dropdowns/buttons.
sealed class KeyboardInputFilter(ControllerSession session) : IMessageFilter
{
    public bool PreFilterMessage(ref Message message) => session.Source is KeyboardSource &&
        message.Msg is 0x100 or 0x101 or 0x104 or 0x105 or 0x102 &&
        (Keys)(int)message.WParam is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.R or Keys.F;
}
