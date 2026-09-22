using System.ComponentModel;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

if (args.Length == 2 && args[0] == "--profiles-preview")
{
    var preview = new Thread(() => ProfileLoader.Preview(args[1]));
    preview.SetApartmentState(ApartmentState.STA); preview.Start(); preview.Join();
    return 0;
}

if (args.Length == 2 && args[0] == "--profile-check")
{
    try
    {
        var profiles = ProfileSaveReader.Load(args[1]);
        Console.WriteLine($"Read {profiles.Select(p => p.Name).Distinct().Count()} profiles and {profiles.Count} controller presets.");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}

if (args.Length == 2 && args[0] == "--mechanics-preview")
{
    var preview = new Thread(() => MechanicsControls.Preview(args[1]));
    preview.SetApartmentState(ApartmentState.STA); preview.Start(); preview.Join();
    return 0;
}
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
                using var preview = new Panel { Size = new Size(1440, 1080) };
                using var view = new StickView { Dock = DockStyle.Fill };
                view.Session.Source = source;
                using var tuning = new TuningControls();
                using var pages = new UtilityPages(view, tuning) { Dock = DockStyle.Fill };
                preview.Controls.Add(pages);
                using var toolbar = new ControllerToolbar(view) { Dock = DockStyle.Top };
                preview.Controls.Add(toolbar);
                preview.CreateControl(); preview.PerformLayout();
                view.Sample();
                using var bitmap = new Bitmap(preview.ClientSize.Width, preview.ClientSize.Height);
                preview.DrawToBitmap(bitmap, preview.ClientRectangle);
                bitmap.Save("controller-preview.png");
                view.OctagonalGate = true;
                view.LeftDeadzonePercent = 50;
                view.Sample();
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
    RefactorTests.Run();
    ProfileSaveTests.Run();
    await RefactorTests.SessionLifecycle();
    Calibration.SelfTest();
    PercentControl.SelfTest();
    StickProcessing.SelfTest();
    PressDetector.SelfTest();
    PerimeterSweeps.SelfTest();
    Flicks.SelfTest();
    AnalyzerScore.SelfTest();
    PollingRate.SelfTest();
    ModActivation.SelfTest();
    MechanicStateTests.SelfTest();
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
    var uiThread = new Thread(() => MainWindow.Run(seconds));
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
