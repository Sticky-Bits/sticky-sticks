using System.Runtime.InteropServices;

// SDL's gamepad layer, matching the API used by Rivals. No application deadzone here.
sealed class SdlSource : ControllerSource
{
    const uint GamepadSubsystem = 0x2000;
    static bool initialized;
    static string version = "";
    IntPtr gamepad;
    readonly string detail;
    public override string[] Axes => ["LX", "LY", "RX", "RY"];
    public override string Status => "SDL controller disconnected — reconnect and Refresh.";

    static string Text(IntPtr text) => Marshal.PtrToStringUTF8(text) ?? "";
    static void Initialize()
    {
        if (initialized) return;
        SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        if (!SDL_InitSubSystem(GamepadSubsystem)) throw new InvalidOperationException(Text(SDL_GetError()));
        initialized = true;
        // WinForms owns the window/message loop. Poll SDL explicitly instead of queuing events.
        SDL_SetGamepadEventsEnabled(false);
        SDL_SetJoystickEventsEnabled(false);
        string mappings = Path.Combine(AppContext.BaseDirectory, "gamecontrollerdb.txt");
        if (File.Exists(mappings))
            foreach (string line in File.ReadLines(mappings))
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith('#')) SDL_AddGamepadMapping(line);
        int v = SDL_GetVersion();
        version = $"{v / 1000000}.{v / 1000 % 1000}.{v % 1000}";
    }

    public static new IEnumerable<ControllerChoice> Discover()
    {
        var choices = new List<ControllerChoice>();
        try
        {
            Initialize();
            SDL_UpdateGamepads();
            IntPtr ids = SDL_GetGamepads(out int count);
            if (ids == IntPtr.Zero) throw new InvalidOperationException(Text(SDL_GetError()));
            try
            {
                for (int i = 0; i < count; i++)
                {
                    uint id = unchecked((uint)Marshal.ReadInt32(ids, i * 4));
                    string name = Text(SDL_GetGamepadNameForID(id));
                    ushort vendor = SDL_GetGamepadVendorForID(id), product = SDL_GetGamepadProductForID(id);
                    choices.Add(new($"sdl:{id}", $"{name} — SDL {version} #{id} [{vendor:X4}:{product:X4}]", () => new SdlSource(id)));
                }
            }
            finally { SDL_free(ids); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            string message = $"SDL unavailable: {e.Message}. Keep SDL3.dll beside the executable.";
            // Failure of this optional backend must not hide the other controller sources.
            choices.Add(new("sdl:unavailable", "SDL unavailable — select for details", () => throw new InvalidOperationException(message)));
        }
        return choices;
    }

    public SdlSource(uint id)
    {
        Initialize();
        gamepad = SDL_OpenGamepad(id);
        if (gamepad == IntPtr.Zero) throw new InvalidOperationException($"SDL could not open controller: {Text(SDL_GetError())}");
        detail = $"SDL {version} | {Text(SDL_GetGamepadNameForID(id))} | {SDL_GetGamepadVendorForID(id):X4}:{SDL_GetGamepadProductForID(id):X4} | Path: {Text(SDL_GetGamepadPath(gamepad))}";
    }

    public override ControllerFrame? Read()
    {
        if (gamepad == IntPtr.Zero) return null;
        SDL_UpdateGamepads();
        if (!SDL_GamepadConnected(gamepad)) return null;
        var raw = new long[4];
        var values = new double[4];
        for (int i = 0; i < 4; i++)
        {
            short value = SDL_GetGamepadAxis(gamepad, i);
            raw[i] = value;
            values[i] = XInput.Normalize(value);
        }
        return new(values, raw, detail) {
            RightTrigger = Math.Max(0, (int)SDL_GetGamepadAxis(gamepad, 5)) * 255.0 / 32767,
            LeftTrigger = Math.Max(0, (int)SDL_GetGamepadAxis(gamepad, 4)) * 255.0 / 32767 };
    }

    public override ValueTask DisposeAsync()
    {
        if (gamepad != IntPtr.Zero) SDL_CloseGamepad(gamepad);
        gamepad = IntPtr.Zero;
        return ValueTask.CompletedTask;
    }

    public static void Shutdown()
    {
        if (!initialized) return;
        SDL_QuitSubSystem(GamepadSubsystem);
        initialized = false;
    }

    const string Library = "SDL3.dll";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_InitSubSystem(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_QuitSubSystem(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_SetGamepadEventsEnabled([MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_SetJoystickEventsEnabled([MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int SDL_AddGamepadMapping([MarshalAs(UnmanagedType.LPUTF8Str)] string mapping);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int SDL_GetVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetGamepads(out int count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_free(IntPtr memory);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetGamepadNameForID(uint id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern ushort SDL_GetGamepadVendorForID(uint id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern ushort SDL_GetGamepadProductForID(uint id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetGamepadPath(IntPtr gamepad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_OpenGamepad(uint id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_CloseGamepad(IntPtr gamepad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void SDL_UpdateGamepads();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] static extern bool SDL_GamepadConnected(IntPtr gamepad);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern short SDL_GetGamepadAxis(IntPtr gamepad, int axis);
}
