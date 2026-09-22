using System.Text;

sealed record SavedControllerProfile(string Name, string ControllerType, float Deadzone, float Sensitivity, float HardPress, float RightThreshold)
{
    public static int Percent(float value) => (int)Math.Round((double)value * 100, MidpointRounding.AwayFromZero);
    public string Summary => $"Left deadzone: {Percent(Deadzone)}%\nLeft sensitivity: {Percent(Sensitivity)}%\nHard press: {Percent(HardPress)}%\nRight threshold: {Percent(RightThreshold)}%";
}
static class ProfileSaveReader
{
    public const string FileName = "Rivals2_PlayerTagSaveSlot.sav";
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rivals2", "Saved", "SaveGames", FileName);
    public static IReadOnlyList<SavedControllerProfile> Load(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > 32 * 1024 * 1024) throw new InvalidDataException("The profile save is too large to read safely.");
        byte[] data = new byte[checked((int)file.Length)]; file.ReadExactly(data);
        return Parse(data);
    }
    public static IReadOnlyList<SavedControllerProfile> Parse(byte[] data)
    {
        var reader = new Reader(data);
        var root = reader.ReadSave();
        if (!root.TryGetValue("SavedPlayerTags", out var tagsValue) || tagsValue is not List<Dictionary<string, object>> tags)
            throw new InvalidDataException("This file does not contain Rivals player profiles.");
        var result = new List<SavedControllerProfile>();
        foreach (var tag in tags)
        {
            string name = tag.GetValueOrDefault("TagName") as string ?? throw new InvalidDataException("A profile has no name.");
            var controls = Child(Child(tag, "ControlSettings"), "ControllerSettings");
            var banks = Child(controls, "ControllerSettings");
            foreach (var bank in banks)
            {
                var values = bank.Value as Dictionary<string, object> ?? throw new InvalidDataException("Unsupported controller settings.");
                var axis = Child(values, "AxisProperties");
                float Get(Dictionary<string, object> fields, string key, float min, float max)
                {
                    if (fields.GetValueOrDefault(key) is not float value || !float.IsFinite(value) || value < min - .00001f || value > max + .00001f)
                        throw new InvalidDataException($"Profile '{name}' has missing or unsupported {key} values.");
                    return value;
                }
                result.Add(new(name, bank.Key.Replace("EGamepadType::", ""), Get(axis, "DeadZone", .05f, .85f), Get(axis, "Sensitivity", .05f, 3f),
                    Get(values, "HardPressThreshold", .05f, .95f), Get(values, "RightStickThreshold", .05f, .95f)));
            }
        }
        if (result.Count == 0) throw new InvalidDataException("No controller profiles were found in this save.");
        return result.AsReadOnly();
    }
    static Dictionary<string, object> Child(Dictionary<string, object> fields, string key) =>
        fields.GetValueOrDefault(key) as Dictionary<string, object> ?? throw new InvalidDataException($"Unsupported save layout: missing {key}.");

    // UE5 complete property type names. Read tagged payload lengths, never search
    // for strings inside arbitrary mapping data or accidentally use trigger values.
    sealed class Reader(byte[] bytes)
    {
        int position, properties;
        void Need(int count, int end) { if (count < 0 || position > end - count) throw new InvalidDataException("The save is truncated or has an unsupported format."); }
        int Int(int end) { Need(4, end); int value = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4)); position += 4; return value; }
        byte Byte(int end) { Need(1, end); return bytes[position++]; }
        void Skip(int count, int end) { Need(count, end); position += count; }
        string String(int end)
        {
            int count = Int(end);
            if (count == 0) return "";
            if (count < -32768 || count > 65536) throw new InvalidDataException("Invalid save string length.");
            int size = count < 0 ? -count * 2 : count; Need(size, end);
            var encoding = count < 0 ? new UnicodeEncoding(false, false, true) : (Encoding)new UTF8Encoding(false, true);
            string value = encoding.GetString(bytes, position, size); position += size;
            if (!value.EndsWith('\0')) throw new InvalidDataException("Invalid save string terminator.");
            return value.TrimEnd('\0');
        }
        string Type(int end, int depth = 0)
        {
            if (depth > 8) throw new InvalidDataException("Unsupported property nesting.");
            string name = String(end); int children = Int(end);
            if (children < 0 || children > 16) throw new InvalidDataException("Unsupported property type.");
            for (int i = 0; i < children; i++) Type(end, depth + 1);
            return name;
        }
        public Dictionary<string, object> ReadSave()
        {
            int end = bytes.Length;
            if (Int(end) != 0x53415647 || Int(end) != 3) throw new InvalidDataException("This is not a supported Rivals 2 save file.");
            Skip(8 + 10, end); String(end); // Package versions and saved engine version.
            if (Int(end) != 3) throw new InvalidDataException("Unsupported save version format.");
            int versions = Int(end); if (versions < 0 || versions > 1024) throw new InvalidDataException("Invalid save header.");
            Skip(versions * 20, end);
            if (String(end) != "/Script/Rivals2.PlayerTagSaveGame" || Byte(end) != 0)
                throw new InvalidDataException("Choose the Rivals2_PlayerTagSaveSlot.sav profile file.");
            return Properties(end);
        }
        Dictionary<string, object> Properties(int end, int depth = 0)
        {
            if (depth > 32) throw new InvalidDataException("Too many nested save properties.");
            var fields = new Dictionary<string, object>();
            while (position < end)
            {
                if (++properties > 100000) throw new InvalidDataException("Too many saved properties.");
                string name = String(end); if (name == "None") return fields;
                string type = Type(end); int size = Int(end); byte flags = Byte(end);
                if ((flags & ~0x18) != 0) throw new InvalidDataException("This save uses unsupported property flags.");
                Need(size, end); int stop = position + size;
                object? value = null;
                if (name == "SavedPlayerTags" && type == "ArrayProperty")
                {
                    int count = Int(stop); if (count < 0 || count > 1024) throw new InvalidDataException("Invalid profile count.");
                    var tags = new List<Dictionary<string, object>>();
                    for (int i = 0; i < count; i++) tags.Add(Properties(stop, depth + 1)); value = tags;
                }
                else if (type == "StructProperty" && name is "ControlSettings" or "ControllerSettings" or "AxisProperties") value = Properties(stop, depth + 1);
                else if (type == "MapProperty" && name == "ControllerSettings")
                {
                    if (Int(stop) != 0) throw new InvalidDataException("Unsupported controller map deletions.");
                    int count = Int(stop); if (count < 0 || count > 256) throw new InvalidDataException("Invalid controller count.");
                    var banks = new Dictionary<string, object>();
                    for (int i = 0; i < count; i++) banks.Add(String(stop), Properties(stop, depth + 1)); value = banks;
                }
                else if (type == "StrProperty" && name == "TagName") value = String(stop);
                else if (type == "FloatProperty") { if (size != 4) throw new InvalidDataException("Invalid float property."); value = BitConverter.Int32BitsToSingle(Int(stop)); }
                if (value != null)
                {
                    if (position != stop) throw new InvalidDataException("Unexpected data inside a saved property.");
                    if (!fields.TryAdd(name, value)) throw new InvalidDataException("Duplicate saved property.");
                }
                position = stop;
            }
            throw new InvalidDataException("Missing saved property terminator.");
        }
    }
}

