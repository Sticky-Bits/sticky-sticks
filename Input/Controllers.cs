using System.Runtime.InteropServices;

sealed record ControllerFrame(double[] Values, long[] Raw, string Detail)
{
    public long Timestamp { get; init; } = System.Diagnostics.Stopwatch.GetTimestamp();
    // Common XInput-style range (0..255); null when the backend cannot identify RT.
    public double? RightTrigger { get; init; }
    public double? LeftTrigger { get; init; }
}
sealed record ControllerChoice(string Id, string Name, Func<ControllerSource> Open)
{
    public override string ToString() => Name;
}
abstract class ControllerSource : IAsyncDisposable
{
    public virtual bool HasReportStream => false;
    public event Action<ControllerFrame>? ReportReceived;
    protected void Report(ControllerFrame frame) => ReportReceived?.Invoke(frame);
    public abstract string[] Axes { get; }
    public virtual int[] DefaultMapping => [0, 1, 2, 3];
    public abstract ControllerFrame? Read();
    public virtual string Status => "Disconnected";
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static List<ControllerChoice> Discover()
    {
        var result = new List<ControllerChoice>();
        for (uint i = 0; i < 4; i++)
        {
            uint slot = i;
            if (XInput.TryRead(slot, out _)) result.Add(new($"xinput:{slot}", $"XInput {slot} — Xbox / Steam compatible", () => new XboxSource(slot)));
        }
        int n = 0;
        foreach (var path in Hid.ListControllers())
            result.Add(new(path, $"Switch Pro {++n} — direct USB / stored calibration", () => new SwitchSource(path)));
        result.AddRange(JoystickSource.Discover());
        result.AddRange(SdlSource.Discover());
        return result;
    }
}
sealed class XboxSource(uint slot) : ControllerSource
{
    public override string[] Axes => ["LX", "LY", "RX", "RY"];
    public override ControllerFrame? Read()
    {
        if (!XInput.TryRead(slot, out var s)) return null;
        var g = s.Gamepad;
        return new([XInput.Normalize(g.LX), XInput.Normalize(g.LY), XInput.Normalize(g.RX), XInput.Normalize(g.RY)],
            [g.LX, g.LY, g.RX, g.RY], $"Packet: {s.Packet}   Buttons: 0x{g.Buttons:X4}   LT: {g.LeftTrigger}   RT: {g.RightTrigger}") { RightTrigger = g.RightTrigger, LeftTrigger = g.LeftTrigger };
    }
}
sealed class SwitchSource : ControllerSource
{
    public override bool HasReportStream => true;
    readonly CancellationTokenSource stop = new();
    readonly Task worker;
    volatile ControllerFrame? latest;
    volatile string status = "Reading stored calibration…";
    public override string[] Axes => ["LX", "LY", "RX", "RY"];
    public override string Status => status;
    public override ControllerFrame? Read() => latest;
    public SwitchSource(string path) => worker = Task.Run(async () =>
    {
        Hid? device = null;
        try
        {
            device = Hid.OpenController(path);
            await device.Initialize(stop.Token);
            var c = await device.ReadCalibration(stop.Token);
            while (!stop.IsCancellationRequested)
            {
                var r = await device.Read(TimeSpan.FromSeconds(3), stop.Token);
                if (r.Length < 12 || r[0] != 0x30) continue;
                int lx = r[6] | ((r[7] & 15) << 8), ly = (r[7] >> 4) | (r[8] << 4);
                int rx = r[9] | ((r[10] & 15) << 8), ry = (r[10] >> 4) | (r[11] << 4);
                latest = new([c.Left.X.Normalize(lx), c.Left.Y.Normalize(ly), c.Right.X.Normalize(rx), c.Right.Y.Normalize(ry)],
                    [lx, ly, rx, ry], $"Calibration: L {c.Left.Source} / R {c.Right.Source}   Centers: {c.Left.X.Center}, {c.Left.Y.Center}, {c.Right.X.Center}, {c.Right.Y.Center}")
                    { RightTrigger = (r[3] & 0x80) != 0 ? 255 : 0, LeftTrigger = (r[5] & 0x80) != 0 ? 255 : 0 }; // ZR/ZL are digital.
                Report(latest);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception e) { status = e.Message; }
        finally
        {
            latest = null;
            if (device != null)
            {
                try { await device.Send([0x80, 0x05], CancellationToken.None); } catch { }
                device.Dispose();
            }
        }
    });
    public override async ValueTask DisposeAsync() { stop.Cancel(); await worker; stop.Dispose(); }
}

// Windows joystick driver interface: supports named devices and up to six reported axes.
sealed class JoystickSource(uint id, JoystickSource.Caps caps) : ControllerSource
{
    readonly int[] indices = Enumerable.Range(0, 6).Where(i => i < 2 || (caps.Flags & (1u << (i - 2))) != 0).ToArray();
    public override string[] Axes => indices.Select(i => new[] { "X", "Y", "Z", "R", "U", "V" }[i]).ToArray();
    public override int[] DefaultMapping => [0, 1, Array.IndexOf(indices, 3), Array.IndexOf(indices, 4)];
    public override ControllerFrame? Read()
    {
        var s = new Info { Size = 52, Flags = 0xFF }; // No JOY_USEDEADZONE or automatic centering.
        if (joyGetPosEx(id, ref s) != 0) return null;
        uint[] raw = [s.X, s.Y, s.Z, s.R, s.U, s.V];
        uint[] min = [caps.XMin, caps.YMin, caps.ZMin, caps.RMin, caps.UMin, caps.VMin];
        uint[] max = [caps.XMax, caps.YMax, caps.ZMax, caps.RMax, caps.UMax, caps.VMax];
        return new(indices.Select(i => max[i] > min[i] ? Math.Clamp(2.0 * ((double)raw[i] - min[i]) / (max[i] - min[i]) - 1, -1, 1) : 0).ToArray(),
            indices.Select(i => (long)raw[i]).ToArray(), $"Buttons: 0x{s.Buttons:X8}   POV: {s.Pov}   Driver ranges; choose axis mappings above");
    }
    public static new IEnumerable<ControllerChoice> Discover()
    {
        for (uint i = 0; i < joyGetNumDevs(); i++)
        {
            uint slot = i;
            var info = new Info { Size = 52, Flags = 0xFF };
            if (joyGetPosEx(slot, ref info) != 0 || joyGetDevCaps((UIntPtr)slot, out var c, (uint)Marshal.SizeOf<Caps>()) != 0) continue;
            yield return new($"joystick:{slot}:{c.Name}", $"{DeviceNames.Joystick(slot, c.RegKey, c.Name)} — Windows joystick {slot}", () => new JoystickSource(slot, c));
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Info { public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Caps
    {
        public ushort Mid, Pid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint XMin, XMax, YMin, YMax, ZMin, ZMax, Buttons, PeriodMin, PeriodMax, RMin, RMax, UMin, UMax, VMin, VMax, Flags, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Oem;
    }
    [DllImport("winmm.dll")] static extern uint joyGetNumDevs();
    [DllImport("winmm.dll")] static extern uint joyGetPosEx(uint id, ref Info info);
    [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW", CharSet = CharSet.Unicode)] static extern uint joyGetDevCaps(UIntPtr id, out Caps caps, uint size);
}
