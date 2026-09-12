using System.ComponentModel;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

if (args.Length == 2 && args[0] is "--analyzer-preview" or "--flicks-preview")
{
    var preview = new Thread(() => ThumbstickAnalyzer.Preview(args[1], args[0] == "--flicks-preview"));
    preview.SetApartmentState(ApartmentState.STA); preview.Start(); preview.Join();
    return 0;
}
if (args.Length == 2 && args[0] == "--analyzer-smoke-test")
{
    var check = new Thread(() => ThumbstickAnalyzer.SmokeTest(args[1]));
    check.SetApartmentState(ApartmentState.STA); check.Start(); check.Join();
    return 0;
}
if (args.Length == 2 && args[0] == "--analyzer-capture-check")
{
    var checks = new List<string>();
    foreach (var choice in SdlSource.Discover().Where(c => c.Id != "sdl:unavailable"))
    {
        await using var source = choice.Open();
        var stage = new PerimeterSweeps();
        var capture = new AnalyzerCapture(source, [0, 1, 2, 3], new bool[4], stage.Observe);
        await Task.Delay(2000);
        await capture.StopAsync();
        var rates = capture.Rates();
        var bins = stage.Snapshot();
        checks.Add($"{choice.Name}: left {bins.Left.Filled} / right {bins.Right.Filled} populated bins, {rates.ReadHz:F0} API reads/s, {rates.ChangeHz:F1} changes/s; {capture.Error ?? "no error"}");
    }
    SdlSource.Shutdown();
    File.WriteAllLines(args[1], checks.Count > 0 ? checks : ["No SDL controllers available for the capture check."]);
    return 0;
}

if (args.Contains("--diagnostics") || args.Contains("--sdl-diagnostics"))
{
    var results = new List<string>();
    foreach (var choice in args.Contains("--sdl-diagnostics") ? SdlSource.Discover() : ControllerSource.Discover())
    {
        await using var source = choice.Open();
        ControllerFrame? frame = null;
        for (int attempt = 0; attempt < 80 && frame == null; attempt++)
        {
            await Task.Delay(100);
            frame = source.Read();
        }
        results.Add($"{choice.Name}: {(frame == null ? source.Status : $"Normalized: {string.Join(", ", frame.Values)} | Integers: {string.Join(", ", frame.Raw)} | {frame.Detail}")}");
        if (source is SwitchSource && frame != null)
        {
            var render = new Thread(() =>
            {
                using var preview = new Panel { Size = new Size(1100, 900) };
                using var view = new StickView { Dock = DockStyle.Fill, Source = source };
                preview.Controls.Add(view);
                using var toolbar = new ControllerToolbar(view) { Dock = DockStyle.Top };
                preview.Controls.Add(toolbar);
                using var tuning = new TuningControls { Dock = DockStyle.Bottom };
                preview.Controls.Add(tuning);
                preview.CreateControl(); preview.PerformLayout();
                view.Sample();
                using var bitmap = new Bitmap(preview.ClientSize.Width, preview.ClientSize.Height);
                preview.DrawToBitmap(bitmap, preview.ClientRectangle);
                bitmap.Save("controller-preview.png");
                view.OctagonalGate = true;
                view.LeftDeadzonePercent = 50;
                preview.DrawToBitmap(bitmap, preview.ClientRectangle);
                bitmap.Save("controller-octagon-preview.png");
            });
            render.SetApartmentState(ApartmentState.STA); render.Start(); render.Join();
        }
    }
    File.WriteAllLines("controller-diagnostics.txt", results);
    SdlSource.Shutdown();
    return 0;
}

if (args.Contains("--self-test"))
{
    try
    {
    // Known packed values exercise both halves of the shared middle byte.
    var decoded = Decode(new byte[] { 0x30, 0, 0, 0, 0, 0, 0xBC, 0x3A, 0x12, 0xFF, 0x0F, 0 });
    if (decoded != (0xABC, 0x123, 4095, 0)) throw new Exception("Stick decoding failed.");
    Calibration.SelfTest();
    PercentControl.SelfTest();
    StickProcessing.SelfTest();
    PressDetector.SelfTest();
    PerimeterSweeps.SelfTest();
    Flicks.SelfTest();
    AnalyzerScore.SelfTest();
    PollingRate.SelfTest();
    TriggerStartLatch.SelfTest();
    await AnalyzerCapture.SelfTest();
    Console.WriteLine("Stick decoding and calibration tests passed.");
    return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return 1;
    }
}

int seconds = 0;
bool xinputOnly = args.Contains("--xinput");
bool xinputConsole = args.Contains("--xinput-console");
bool usbConsole = args.Contains("--usb-console");
args = args.Where(a => a != "--xinput" && a != "--xinput-console" && a != "--usb-console").ToArray();
if (args.Length != 0 && (args.Length != 2 || args[0] != "--seconds" ||
    !int.TryParse(args[1], out seconds) || seconds <= 0))
{
    Console.Error.WriteLine("Usage: StickySticks.exe [--xinput | --xinput-console] [--seconds N] | --self-test");
    return 1;
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
if (!xinputConsole && !usbConsole)
{
    // Own the focused window in this process, rather than relying on the terminal host.
    var uiThread = new Thread(() => XInputWindow.Run(seconds));
    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    uiThread.Join();
    return 0;
}
if (xinputConsole)
{
    Console.WriteLine("XInput normalized and raw readings (may include Steam processing). Ctrl+C to exit.");
    if (seconds > 0) stop.CancelAfter(TimeSpan.FromSeconds(seconds));
    try
    {
        while (true)
        {
            Console.WriteLine(XInput.Describe());
            await Task.Delay(100, stop.Token);
        }
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    return 0;
}
try
{
    using var device = Hid.OpenController();
    Console.WriteLine("Nintendo Switch Pro Controller connected (USB 057E:2009).");
    Console.WriteLine("Initializing USB reports...");
    var clock = Stopwatch.StartNew();
    long lastPrint = -100;
    int samples = 0;
    try
    {
        await device.Initialize(stop.Token);
        var calibration = await device.ReadCalibration(stop.Token);
        Console.WriteLine($"Left calibration ({calibration.Left.Source}): X {calibration.Left.X}; Y {calibration.Left.Y}");
        Console.WriteLine($"Right calibration ({calibration.Right.Source}): X {calibration.Right.X}; Y {calibration.Right.Y}");
        Console.WriteLine("Normalized -1..+1, no dead zone or smoothing; only endpoint clamping. Raw values included.");
        Console.WriteLine("Move either stick. Press Ctrl+C to exit.");
        if (seconds > 0) stop.CancelAfter(TimeSpan.FromSeconds(seconds));
        while (true)
        {
            var report = await device.Read(TimeSpan.FromSeconds(3), stop.Token);
            if (report.Length < 12 || report[0] != 0x30) continue;
            var (lx, ly, rx, ry) = Decode(report);
            samples++;
            if (clock.ElapsedMilliseconds - lastPrint < 50) continue;
            lastPrint = clock.ElapsedMilliseconds;
            string line = FormattableString.Invariant($"USB  LX: {calibration.Left.X.Normalize(lx),8:F5}  LY: {calibration.Left.Y.Normalize(ly),8:F5}  RX: {calibration.Right.X.Normalize(rx),8:F5}  RY: {calibration.Right.Y.Normalize(ry),8:F5}  Raw: {lx},{ly},{rx},{ry}  Reports: {samples} | {XInput.Describe()}");
            Console.WriteLine(line);
        }
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    finally
    {
        Console.WriteLine();
        // Restore the controller's USB timeout when exiting normally.
        try { await device.Send(new byte[] { 0x80, 0x05 }, CancellationToken.None); }
        catch (Exception) { /* Device may already have been unplugged. */ }
    }
    if (samples == 0) throw new IOException("No raw stick reports were received.");
    return 0;
}

catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
catch (Exception e)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    Console.Error.WriteLine("Check the USB data cable. If necessary, close Steam/BetterJoy or other controller apps and reconnect the controller.");
    return 1;
}

static (int LX, int LY, int RX, int RY) Decode(ReadOnlySpan<byte> r) =>
    (r[6] | ((r[7] & 15) << 8), (r[7] >> 4) | (r[8] << 4),
     r[9] | ((r[10] & 15) << 8), (r[10] >> 4) | (r[11] << 4));

sealed class Hid : IDisposable
{
    readonly FileStream stream;
    readonly int inputLength, outputLength;
    byte packetNumber;
    Hid(SafeFileHandle handle, int input, int output)
    {
        stream = new FileStream(handle, FileAccess.ReadWrite, input, isAsync: true);
        inputLength = input;
        outputLength = output;
    }

    public static Hid OpenController(string? path = null) => FindController(path, null)!;

    public static List<string> ListControllers()
    {
        var paths = new List<string>();
        FindController(null, paths);
        return paths;
    }

    static Hid? FindController(string? selectedPath, List<string>? paths)
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        var failures = new List<string>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref info))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetail(set, ref info, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                var detail = Marshal.AllocHGlobal((int)size);
                string path;
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref info, detail, size, out _, IntPtr.Zero))
                        throw new Win32Exception();
                    path = Marshal.PtrToStringUni(detail + 4)!;
                }
                finally { Marshal.FreeHGlobal(detail); }
                if (!path.Contains("vid_057e&pid_2009", StringComparison.OrdinalIgnoreCase)) continue;
                if (selectedPath != null && path != selectedPath) continue;
                var handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    failures.Add(new Win32Exception().Message);
                    handle.Dispose();
                    continue;
                }
                try
                {
                    if (!HidD_GetPreparsedData(handle, out var data)) throw new Win32Exception();
                    var caps = Marshal.AllocHGlobal(64);
                    int input, output;
                    try
                    {
                        if (HidP_GetCaps(data, caps) != 0x00110000) throw new IOException("Cannot read HID capabilities.");
                        input = (ushort)Marshal.ReadInt16(caps, 4);
                        output = (ushort)Marshal.ReadInt16(caps, 6);
                    }
                    finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(data); }
                    // Original Pro Controller USB reports are 64 bytes. Bluetooth uses smaller reports.
                    if (input < 64 || output < 64) { handle.Dispose(); continue; }
                    if (paths != null) { paths.Add(path); handle.Dispose(); continue; }
                    return new Hid(handle, input, output);
                }
                catch { handle.Dispose(); throw; }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        if (paths != null) return null;
        throw new IOException(failures.Count > 0
            ? "Controller found but could not be opened: " + string.Join("; ", failures)
            : "No original Nintendo Switch Pro Controller found over USB (VID 057E, PID 2009).");
    }

    public async Task Send(byte[] command, CancellationToken token)
    {
        var report = new byte[outputLength];
        command.CopyTo(report, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await stream.WriteAsync(report, timeout.Token);
    }

    public async Task<byte[]> Read(TimeSpan wait, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(wait);
        var report = new byte[inputLength];
        try
        {
            int count = await stream.ReadAsync(report, timeout.Token);
            if (count == 0) throw new IOException("Controller disconnected.");
            return report[..count];
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Timed out waiting for controller reports."); }
    }

    public async Task Initialize(CancellationToken token)
    {
        foreach (byte command in new byte[] { 0x02, 0x03, 0x02 })
        {
            await Send(new byte[] { 0x80, command }, token);
            var timer = Stopwatch.StartNew();
            try
            {
                while (timer.Elapsed < TimeSpan.FromSeconds(1))
                {
                    var r = await Read(TimeSpan.FromSeconds(1) - timer.Elapsed, token);
                    if (r.Length >= 2 && r[0] == 0x81 && r[1] == command) break;
                }
            }
            catch (TimeoutException) { /* An already initialized controller may not ACK again. */ }
        }
        await Send(new byte[] { 0x80, 0x04 }, token);
        // Subcommand 03 selects full report mode 30; rumble bytes specify zero amplitude.
        await SendSubcommand(0x03, new byte[] { 0x30 }, token);
    }

    Task SendSubcommand(byte command, byte[] data, CancellationToken token)
    {
        var report = new byte[11 + data.Length];
        new byte[] { 0x01, (byte)(packetNumber++ & 15), 0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40, command }.CopyTo(report, 0);
        data.CopyTo(report, 11);
        return Send(report, token);
    }

    public async Task<Calibration> ReadCalibration(CancellationToken token)
    {
        var factory = await ReadSpi(0x603D, 18, token);
        var user = await ReadSpi(0x8010, 22, token);
        return Calibration.Parse(factory, user);
    }

    async Task<byte[]> ReadSpi(uint address, byte length, CancellationToken token)
    {
        var request = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(request, address);
        request[4] = length;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await SendSubcommand(0x10, request, token);
            var timer = Stopwatch.StartNew();
            while (true)
            {
                var remaining = TimeSpan.FromSeconds(1) - timer.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                byte[] reply;
                try { reply = await Read(remaining, token); }
                catch (TimeoutException) { break; }
                // Ignore streaming input and unrelated replies; match ACK, address and size.
                if (reply.Length < 20 || reply[0] != 0x21 || reply[14] != 0x10) continue;
                if ((reply[13] & 0x80) == 0) throw new IOException("Controller rejected calibration read.");
                if (BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(15)) != address || reply[19] != length) continue;
                if (reply.Length < 20 + length) throw new IOException("Truncated calibration reply.");
                return reply[20..(20 + length)];
            }
        }
        throw new TimeoutException($"Could not read controller calibration at 0x{address:X4}.");
    }

    public void Dispose() => stream.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    struct InterfaceData { public int Size; public Guid Guid; public int Flags; public UIntPtr Reserved; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, IntPtr caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData info, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}

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



