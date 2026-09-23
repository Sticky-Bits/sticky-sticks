using System.Collections.Concurrent;
static class GameCubeTests
{
    static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
    static byte[] Packet(int port, byte type = 0x10)
    {
        var report = new byte[37]; report[0] = 0x21;
        int offset = 1 + port * 9; report[offset] = type;
        report[offset + 3] = 128; report[offset + 4] = 216; report[offset + 5] = 40; report[offset + 6] = 255;
        report[offset + 7] = 40; report[offset + 8] = 128;
        return report;
    }
    public static async Task Run()
    {
        GameCubeUsb.VerifyLayouts();
        for (int port = 0; port < 4; port++)
        foreach (byte type in new byte[] { 0x10, 0x20, 0x14 })
        {
            var packet = Packet(port, type);
            var decoded = GameCubeSource.Decode(packet, port)!;
            Require(decoded.Values.SequenceEqual(new[] { 0.0, 1, -1, 127 / 88.0 }), "GameCube stick decoding/scaling failed.");
            Require(decoded.Raw.SequenceEqual(new long[] { 128, 216, 40, 255 }), "GameCube original bytes lost.");
            Require(decoded.LeftTrigger == 0 && decoded.RightTrigger == 127.5, "GameCube analog triggers failed.");
            for (int other = 0; other < 4; other++) if (other != port) Require(GameCubeSource.Decode(packet, other) == null, "GameCube ports mixed.");
            packet[1 + port * 9 + 2] = 0x0c;
            decoded = GameCubeSource.Decode(packet, port)!;
            Require(decoded.LeftTrigger == 255 && decoded.RightTrigger == 255, "Digital trigger clicks failed.");
            packet[1 + port * 9] = 4;
            Require(GameCubeSource.Decode(packet, port) == null, "Power bit mistaken for a connected controller.");
        }
        Require(GameCubeSource.Decode(new byte[37], 0) == null && GameCubeSource.Decode(Packet(0)[..36], 0) == null, "Malformed report accepted.");
        Require(GameCubeSource.Normalize(129) == 1 / 88.0 && GameCubeSource.Normalize(0) < -1, "Source hides jitter or clips overshoot.");
        var usb = new FakeConnection();
        var source = new GameCubeSource(usb, 2);
        var observed = new ConcurrentQueue<AnalyzerReading>();
        var capture = new AnalyzerCapture(source, [0, 1, 2, 3], new bool[4], observed.Enqueue);
        async Task Until(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(3000);
            while (!condition()) await Task.Delay(5, timeout.Token);
        }
        try
        {
            usb.Send(Packet(2)); usb.Send(Packet(2));
            await Until(() => observed.Count == 2);
            Require(observed.All(r => r.FreshLeft && r.FreshRight), "Identical GameCube USB reports were discarded.");
            usb.Send(Packet(2, 0)); await Until(() => source.Read() == null);
            usb.Send(Packet(2)); await Until(() => observed.Count == 3 && source.Read() != null);
            await Until(() => source.Read() == null); // no reports for 500 ms clears stale values
            usb.Send(Packet(2)); await Until(() => observed.Count == 4 && source.Read() != null);
            usb.Disconnect(); await Until(() => usb.Disposals == 1);
            Require(source.Read() == null && source.Status.Contains("disconnected"), "Adapter disconnect retained stale input.");
        }
        finally { await capture.StopAsync(); await source.DisposeAsync(); await source.DisposeAsync(); }
        Require(usb.Disposals == 1, "USB ownership disposed more than once.");
        var idle = new FakeConnection();
        await new GameCubeSource(idle, 0).DisposeAsync();
        Require(idle.Disposals == 1, "Cancellation failed to release USB ownership.");
    }
    sealed class FakeConnection : GameCubeUsb.IConnection
    {
        readonly BlockingCollection<byte[]?> packets = new();
        public int Disposals;
        public void Send(byte[] packet) => packets.Add(packet);
        public void Disconnect() => packets.Add(null);
        public int Receive(byte[] buffer, out int transferred)
        {
            transferred = 0;
            if (!packets.TryTake(out var packet, 100)) return -7;
            if (packet == null) return -4;
            packet.CopyTo(buffer, 0); transferred = packet.Length; return 0;
        }
        public void Dispose() { Interlocked.Increment(ref Disposals); packets.Dispose(); }
    }
}
