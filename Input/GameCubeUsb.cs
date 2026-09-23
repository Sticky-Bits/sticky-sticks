using System.Runtime.InteropServices;

// Minimal dynamic libusb binding. Discovery never opens/claims an adapter.
static class GameCubeUsb
{
    const string Library = "libusb-1.0.dll";
    internal sealed record Adapter(byte Bus, string Ports)
    {
        public string Id => $"{Bus}:{Ports}";
    }
    internal interface IConnection : IDisposable
    {
        int Receive(byte[] buffer, out int transferred);
    }
    public static IReadOnlyList<Adapter> Discover()
    {
        Check(libusb_init(out var context), "initialize libusb");
        try
        {
            var found = new List<Adapter>();
            Enumerate(context, device =>
            {
                if (Matches(device)) found.Add(Identity(device));
            });
            return found;
        }
        finally { libusb_exit(context); }
    }
    static bool Matches(IntPtr device) => libusb_get_device_descriptor(device, out var d) == 0 && d.Vendor == 0x057e && d.Product == 0x0337;
    static Adapter Identity(IntPtr device)
    {
        byte[] path = new byte[8]; int count = libusb_get_port_numbers(device, path, path.Length);
        return new(libusb_get_bus_number(device), count > 0 ? string.Join('.', path.Take(count)) : $"address-{libusb_get_device_address(device)}");
    }
    static void Enumerate(IntPtr context, Action<IntPtr> visit)
    {
        long count = libusb_get_device_list(context, out var list).ToInt64();
        if (count < 0) Check((int)count, "list USB devices");
        try { for (long i = 0; i < count; i++) visit(Marshal.ReadIntPtr(list, checked((int)i * IntPtr.Size))); }
        finally { libusb_free_device_list(list, 1); }
    }
    public static IConnection Open(Adapter adapter) => new Connection(adapter);
    sealed class Connection : IConnection
    {
        IntPtr context, handle;
        bool claimed;
        byte input;
        public Connection(Adapter adapter)
        {
            try
            {
                Check(libusb_init(out context), "initialize libusb");
                byte output = 0; int alternate = 0;
                Enumerate(context, device =>
                {
                    if (handle != IntPtr.Zero || !Matches(device) || Identity(device) != adapter) return;
                    Check(libusb_open(device, out handle), "open GameCube adapter");
                    Check(libusb_get_config_descriptor(device, 0, out var config), "read USB configuration");
                    try
                    {
                        var c = Marshal.PtrToStructure<ConfigDescriptor>(config);
                        for (int i = 0; i < c.InterfaceCount; i++)
                        {
                            var group = Marshal.PtrToStructure<Interface>(c.Interfaces + i * Marshal.SizeOf<Interface>());
                            for (int a = 0; a < group.Count; a++)
                            {
                                var face = Marshal.PtrToStructure<InterfaceDescriptor>(group.Alternates + a * Marshal.SizeOf<InterfaceDescriptor>());
                                if (face.Number != 0) continue;
                                byte candidateIn = 0, candidateOut = 0;
                                for (int e = 0; e < face.EndpointCount; e++)
                                {
                                    var endpoint = Marshal.PtrToStructure<EndpointDescriptor>(face.Endpoints + e * Marshal.SizeOf<EndpointDescriptor>());
                                    if ((endpoint.Attributes & 3) != 3) continue; // interrupt only
                                    if ((endpoint.Address & 0x80) != 0) candidateIn = endpoint.Address;
                                    else candidateOut = endpoint.Address;
                                }
                                if (candidateIn != 0 && candidateOut != 0)
                                { input = candidateIn; output = candidateOut; alternate = face.Alternate; return; }
                            }
                        }
                    }
                    finally { libusb_free_config_descriptor(config); }
                });
                if (handle == IntPtr.Zero) throw new IOException("GameCube adapter disconnected. Reconnect it and click Refresh.");
                if (input == 0 || output == 0) throw new IOException("Interface 0 has no supported interrupt IN/OUT endpoint pair.");
                Check(libusb_claim_interface(handle, 0), "claim GameCube adapter interface 0"); claimed = true;
                if (alternate != 0) Check(libusb_set_interface_alt_setting(handle, 0, alternate), "select USB alternate interface");
                Check(libusb_interrupt_transfer(handle, output, [0x13], 1, out int written, 1000), "start GameCube reports");
                if (written != 1) throw new IOException("GameCube adapter did not accept the polling command.");
            }
            catch { Dispose(); throw; }
        }
        public int Receive(byte[] buffer, out int transferred) => libusb_interrupt_transfer(handle, input, buffer, buffer.Length, out transferred, 100);
        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                if (claimed) libusb_release_interface(handle, 0);
                libusb_close(handle); handle = IntPtr.Zero; claimed = false;
            }
            if (context != IntPtr.Zero) { libusb_exit(context); context = IntPtr.Zero; }
        }
    }
    static void Check(int error, string action)
    {
        if (error < 0) throw new IOException($"Could not {action}: {Marshal.PtrToStringAnsi(libusb_error_name(error))} ({error}). Use Wii U mode and the WinUSB driver for WUP-028 (057E:0337); close Dolphin/other adapter readers.");
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct DeviceDescriptor
    {
        public byte Length, Type; public ushort Usb; public byte Class, Subclass, Protocol, MaxPacket;
        public ushort Vendor, Product, Device; public byte Manufacturer, ProductString, Serial, Configurations;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ConfigDescriptor
    {
        public byte Length, Type; public ushort TotalLength; public byte InterfaceCount, Value, String, Attributes, Power;
        public IntPtr Interfaces, Extra; public int ExtraLength;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Interface { public IntPtr Alternates; public int Count; }
    [StructLayout(LayoutKind.Sequential)]
    struct InterfaceDescriptor
    {
        public byte Length, Type, Number, Alternate, EndpointCount, Class, Subclass, Protocol, String;
        public IntPtr Endpoints, Extra; public int ExtraLength;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct EndpointDescriptor
    {
        public byte Length, Type, Address, Attributes; public ushort MaxPacket; public byte Interval, Refresh, SynchAddress;
        public IntPtr Extra; public int ExtraLength;
    }
    internal static void VerifyLayouts()
    {
        if (IntPtr.Size != 8 || Marshal.SizeOf<DeviceDescriptor>() != 18 || Marshal.SizeOf<ConfigDescriptor>() != 40 ||
            Marshal.SizeOf<Interface>() != 16 || Marshal.SizeOf<InterfaceDescriptor>() != 40 || Marshal.SizeOf<EndpointDescriptor>() != 32)
            throw new Exception("Unexpected x64 libusb descriptor layout.");
    }
    [DllImport(Library)] static extern int libusb_init(out IntPtr context);
    [DllImport(Library)] static extern void libusb_exit(IntPtr context);
    [DllImport(Library)] static extern IntPtr libusb_get_device_list(IntPtr context, out IntPtr devices);
    [DllImport(Library)] static extern void libusb_free_device_list(IntPtr devices, int unref);
    [DllImport(Library)] static extern int libusb_get_device_descriptor(IntPtr device, out DeviceDescriptor descriptor);
    [DllImport(Library)] static extern byte libusb_get_bus_number(IntPtr device);
    [DllImport(Library)] static extern byte libusb_get_device_address(IntPtr device);
    [DllImport(Library)] static extern int libusb_get_port_numbers(IntPtr device, [Out] byte[] ports, int length);
    [DllImport(Library)] static extern int libusb_open(IntPtr device, out IntPtr handle);
    [DllImport(Library)] static extern void libusb_close(IntPtr handle);
    [DllImport(Library)] static extern int libusb_claim_interface(IntPtr handle, int number);
    [DllImport(Library)] static extern int libusb_release_interface(IntPtr handle, int number);
    [DllImport(Library)] static extern int libusb_get_config_descriptor(IntPtr device, byte index, out IntPtr descriptor);
    [DllImport(Library)] static extern void libusb_free_config_descriptor(IntPtr descriptor);
    [DllImport(Library)] static extern int libusb_set_interface_alt_setting(IntPtr handle, int number, int alternate);
    [DllImport(Library)] static extern int libusb_interrupt_transfer(IntPtr handle, byte endpoint, [In, Out] byte[] data, int length, out int transferred, uint timeout);
    [DllImport(Library)] static extern IntPtr libusb_error_name(int error);
}
