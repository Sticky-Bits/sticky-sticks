using Microsoft.Win32;

static class DeviceNames
{
    public static string Joystick(uint slot, string driverKey, string fallback)
    {
        // Resolve the driver's slot-to-OEM association; never guess a name from another device.
        const string root = @"System\CurrentControlSet\Control\";
        try
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var settings = hive.OpenSubKey(root + @"MediaResources\Joystick\" + driverKey + @"\CurrentJoystickSettings");
                if (settings?.GetValue($"Joystick{slot + 1}OEMName") is not string oem) continue;
                foreach (var namesHive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    using var names = namesHive.OpenSubKey(root + @"MediaProperties\PrivateProperties\Joystick\OEM\" + oem);
                    if (names?.GetValue("OEMName") is string name && !string.IsNullOrWhiteSpace(name)) return name.Trim();
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return fallback;
    }
}
