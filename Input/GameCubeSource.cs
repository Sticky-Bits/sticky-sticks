using System.Diagnostics;

sealed class GameCubeSource : ControllerSource
{
    readonly GameCubeUsb.IConnection connection;
    readonly CancellationTokenSource stop = new();
    readonly Task worker;
    readonly int port;
    int disposed;
    volatile ControllerFrame? latest;
    volatile string status = "Waiting for GameCube reports…";
    public override string[] Axes => ["LX", "LY", "CX", "CY"];
    public override bool HasReportStream => true;
    public override string Status => status;
    public override ControllerFrame? Read() => latest;
    public static new IEnumerable<ControllerChoice> Discover()
    {
        var choices = new List<ControllerChoice>();
        try
        {
            foreach (var adapter in GameCubeUsb.Discover())
                for (int port = 0; port < 4; port++)
                {
                    int selectedPort = port;
                    choices.Add(new($"gamecube:{adapter.Id}:{port}", $"GameCube adapter {adapter.Id} · Port {port + 1} — direct USB",
                        () => new GameCubeSource(GameCubeUsb.Open(adapter), selectedPort)));
                }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException)
        {
            string message = $"GameCube USB unavailable: {ex.Message}\nKeep libusb-1.0.dll beside StickySticks.exe.";
            choices.Add(new("gamecube:unavailable", "GameCube USB unavailable — select for details", () => throw new IOException(message)));
        }
        return choices;
    }
    internal GameCubeSource(GameCubeUsb.IConnection connection, int port)
    {
        if (port is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(port));
        this.connection = connection; this.port = port;
        worker = Task.Factory.StartNew(Pump, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    void Pump()
    {
        byte[] report = new byte[37];
        var lastReport = Stopwatch.StartNew();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                int error = connection.Receive(report, out int count);
                if (stop.IsCancellationRequested) break;
                if (error == -4) throw new IOException("GameCube adapter disconnected. Reconnect and click Refresh.");
                if (error != 0 && error != -7) throw new IOException($"GameCube USB read failed ({error}). Close other adapter readers and reconnect.");
                if (error == 0 && count == 37 && report[0] == 0x21)
                {
                    lastReport.Restart();
                    var frame = Decode(report, port);
                    latest = frame;
                    status = frame == null ? $"No controller in GameCube port {port + 1}. Plug one into that port." : "Connected";
                    if (frame != null) Report(frame); // Preserve every USB report, including identical positions.
                }
                else if (lastReport.ElapsedMilliseconds >= 500)
                {
                    latest = null;
                    status = "GameCube adapter stopped reporting. Reconnect and click Refresh.";
                }
            }
        }
        catch (Exception ex) { status = ex.Message; }
        finally { latest = null; connection.Dispose(); }
    }
    // Fixed nominal scale, never learned from motion: center errors, overshoot and
    // jitter remain visible to the analyzer. Y is already positive upward.
    internal static double Normalize(byte value) => (value - 128) / 88.0;
    internal static double Trigger(byte value) => Math.Clamp((value - 40) * 255.0 / 176, 0, 255);
    internal static ControllerFrame? Decode(ReadOnlySpan<byte> report, int port)
    {
        if (port is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(port));
        if (report.Length != 37 || report[0] != 0x21) return null;
        var p = report.Slice(1 + port * 9, 9);
        if ((p[0] & 0x30) == 0) return null;
        byte lx = p[3], ly = p[4], cx = p[5], cy = p[6];
        return new([Normalize(lx), Normalize(ly), Normalize(cx), Normalize(cy)], [lx, ly, cx, cy],
            $"GameCube port {port + 1} · {((p[0] & 0x10) != 0 ? "Wired" : "Wireless")} · Fixed center 128 / range ±88 · Buttons: {p[1]:X2}:{p[2]:X2} · Raw L/R: {p[7]}/{p[8]}")
        {
            LeftTrigger = (p[2] & 0x08) != 0 ? 255 : Trigger(p[7]),
            RightTrigger = (p[2] & 0x04) != 0 ? 255 : Trigger(p[8])
        };
    }
    public override async ValueTask DisposeAsync()
    {
        bool first = Interlocked.Exchange(ref disposed, 1) == 0;
        if (first) stop.Cancel();
        await worker;
        if (first) stop.Dispose();
    }
}
