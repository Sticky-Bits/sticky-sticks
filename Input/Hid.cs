using System.Diagnostics;
using System.ComponentModel;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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


