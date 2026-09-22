// RT starts only on a fresh crossing after observing release. Presses while busy or
// on another page are consumed, never queued for a later start.
sealed class TriggerStartLatch
{
    bool armed;
    public void Reset() => armed = false;
    public bool Update(double? value, bool canStart)
    {
        if (!value.HasValue || !double.IsFinite(value.Value)) { Reset(); return false; }
        if (value.Value <= 25) { armed = true; return false; }
        bool pressed = armed; armed = false;
        return pressed && canStart;
    }
    public static void SelfTest()
    {
        var latch = new TriggerStartLatch();
        if (latch.Update(255, true)) throw new Exception("RT must first observe a release.");
        if (latch.Update(25, true) || !latch.Update(26, true) || latch.Update(255, true))
            throw new Exception("RT threshold / held-input detection failed.");
        latch.Update(0, false);
        if (latch.Update(255, false) || latch.Update(255, true)) throw new Exception("Busy RT press was queued.");
        latch.Update(0, true);
        if (!latch.Update(255, true)) throw new Exception("RT did not re-arm after release.");
        latch.Update(null, true);
        if (latch.Update(255, true)) throw new Exception("RT did not reset after disconnect.");
    }
}
