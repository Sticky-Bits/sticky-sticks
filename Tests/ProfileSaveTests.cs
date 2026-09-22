using System.Text;
static class ProfileSaveTests
{
    static byte[] Bytes(Action<BinaryWriter> action) { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); action(writer); return stream.ToArray(); }
    static void Text(BinaryWriter w, string value) { var bytes = Encoding.UTF8.GetBytes(value + "\0"); w.Write(bytes.Length); w.Write(bytes); }
    static byte[] Properties(params byte[][] values) => Bytes(w => { foreach (var value in values) w.Write(value); Text(w, "None"); });
    static byte[] Property(string name, string type, byte[] payload) => Bytes(w => { Text(w, name); Text(w, type); w.Write(0); w.Write(payload.Length); w.Write((byte)0); w.Write(payload); });
    static byte[] Float(string name, float value) => Property(name, "FloatProperty", Bytes(w => w.Write(value)));
    static byte[] Struct(string name, params byte[][] fields) => Property(name, "StructProperty", Properties(fields));
    static byte[] Bank(float sensitivity) => Properties(Struct("AxisProperties", Float("Sensitivity", sensitivity), Float("DeadZone", .4f)),
        Struct("LeftTriggerProperties", Float("DeadZone", .99f), Float("Sensitivity", 99f)), Float("HardPressThreshold", .8f), Float("RightStickThreshold", .35f));
    static byte[] Save(float sensitivity = 1.234f) => Bytes(w =>
    {
        w.Write(0x53415647); w.Write(3); w.Write(new byte[18]); Text(w, "UE5"); w.Write(3); w.Write(0);
        Text(w, "/Script/Rivals2.PlayerTagSaveGame"); w.Write((byte)0);
        var map = Property("ControllerSettings", "MapProperty", Bytes(m => { m.Write(0); m.Write(2); Text(m, "EGamepadType::Unknown"); m.Write(Bank(sensitivity)); Text(m, "EGamepadType::SwitchPro"); m.Write(Bank(2)); }));
        var unicode = Bytes(n => { n.Write(-5); n.Write(Encoding.Unicode.GetBytes("Test\0")); });
        var tag = Properties(Property("TagName", "StrProperty", unicode), Struct("ControlSettings", Struct("ControllerSettings", map)));
        w.Write(Properties(Property("SavedPlayerTags", "ArrayProperty", Bytes(a => { a.Write(1); a.Write(tag); }))));
    });
    public static void Run()
    {
        var data = Save(); var values = ProfileSaveReader.Parse(data);
        if (values.Count != 2 || values[0].Name != "Test" || values[0].Deadzone != .4f || values[1].Sensitivity != 2 ||
            SavedControllerProfile.Percent(values[0].Sensitivity) != 123 || SavedControllerProfile.Percent(.39999998f) != 40)
            throw new Exception("Profile values, device banks or percentage rounding failed.");
        void Reject(byte[] bytes)
        {
            try { ProfileSaveReader.Parse(bytes); } catch (InvalidDataException) { return; }
            throw new Exception("Invalid save was accepted.");
        }
        Reject(data[..^12]); Reject(new byte[20]); Reject(Save(float.NaN)); Reject(Save(99));
        byte[] wrongVersion = (byte[])data.Clone(); wrongVersion[4] = 99; Reject(wrongVersion);
    }
}
