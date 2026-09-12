// SPI layout: https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/spi_flash_notes.md
readonly record struct AxisCalibration(int Center, int Below, int Above)
{
    public bool IsValid => Center > 0 && Center < 4095 && Below > 0 && Above > 0
        && Center - Below >= 0 && Center + Above <= 4095;

    public double Normalize(int raw) => Math.Clamp(
        (double)(raw - Center) / (raw >= Center ? Above : Below), -1.0, 1.0);

    public override string ToString() => $"min={Center - Below}, center={Center}, max={Center + Above}";
}

sealed record StickCalibration(AxisCalibration X, AxisCalibration Y, string Source)
{
    public bool IsValid => X.IsValid && Y.IsValid;
}

sealed record Calibration(StickCalibration Left, StickCalibration Right)
{
    public static Calibration Parse(ReadOnlySpan<byte> factory, ReadOnlySpan<byte> user)
    {
        if (factory.Length != 18 || user.Length != 22) throw new IOException("Invalid calibration block length.");
        return new(Select(factory[..9], user[..11], true), Select(factory[9..], user[11..], false));
    }

    static StickCalibration Select(ReadOnlySpan<byte> factory, ReadOnlySpan<byte> user, bool left)
    {
        if (user[0] == 0xB2 && user[1] == 0xA1)
        {
            var saved = Decode(user[2..], left, "user");
            if (saved.IsValid) return saved;
        }
        var original = Decode(factory, left, "factory");
        if (!original.IsValid) throw new IOException($"No valid stored calibration for the {(left ? "left" : "right")} stick.");
        return original;
    }

    static StickCalibration Decode(ReadOnlySpan<byte> data, bool left, string source)
    {
        Span<int> values = stackalloc int[6];
        for (int i = 0; i < 3; i++)
        {
            values[i * 2] = data[i * 3] | ((data[i * 3 + 1] & 15) << 8);
            values[i * 2 + 1] = (data[i * 3 + 1] >> 4) | (data[i * 3 + 2] << 4);
        }
        // Left: above, center, below. Right: center, below, above.
        int c = left ? 2 : 0, b = left ? 4 : 2, a = left ? 0 : 4;
        return new(new(values[c], values[b], values[a]), new(values[c + 1], values[b + 1], values[a + 1]), source);
    }

    public static void SelfTest()
    {
        // Distinct X/Y and positive/negative ranges detect ordering or nibble errors.
        byte[] left = { 0xF7, 0x44, 0x42, 0x9F, 0x07, 0x8A, 0x10, 0x95, 0x47 };
        byte[] right = { 0x9F, 0x07, 0x8A, 0x10, 0x95, 0x47, 0xF7, 0x44, 0x42 };
        byte[] factory = [.. left, .. right];
        byte[] user = Enumerable.Repeat((byte)0xFF, 22).ToArray();
        var cal = Parse(factory, user);
        if (cal.Left.X != new AxisCalibration(0x79F, 0x510, 0x4F7) ||
            cal.Left.Y != new AxisCalibration(0x8A0, 0x479, 0x424) ||
            cal.Left != cal.Right || cal.Left.Source != "factory") throw new Exception("Factory calibration decoding failed.");
        user[0] = 0xB2; user[1] = 0xA1;
        left.CopyTo(user, 2);
        user[5] = 0xA0; // Change saved left X center from 1951 to 1952.
        cal = Parse(factory, user);
        if (cal.Left.Source != "user" || cal.Left.X.Center != 1952 || cal.Right.Source != "factory")
            throw new Exception("Per-stick user calibration selection failed.");
        user[11] = 0xB2; user[12] = 0xA1;
        right.CopyTo(user, 13);
        if (Parse(factory, user).Right.Source != "user") throw new Exception("Right user calibration selection failed.");
        Array.Fill(user, (byte)0, 2, 9);
        if (Parse(factory, user).Left.Source != "factory") throw new Exception("Invalid user calibration fallback failed.");
        bool rejected = false;
        try { Parse(new byte[18], new byte[22]); }
        catch (IOException) { rejected = true; }
        if (!rejected) throw new Exception("Invalid calibration was accepted.");
        var axis = new AxisCalibration(2000, 1000, 1500);
        if (axis.Normalize(1000) != -1 || axis.Normalize(2000) != 0 || axis.Normalize(3500) != 1 ||
            axis.Normalize(1500) != -0.5 || axis.Normalize(2750) != 0.5 ||
            axis.Normalize(1999) != -0.001 || axis.Normalize(2001) != 1.0 / 1500 ||
            axis.Normalize(0) != -1 || axis.Normalize(4095) != 1)
            throw new Exception("Normalization/no-dead-zone test failed.");
    }
}
