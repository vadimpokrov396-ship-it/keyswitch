namespace KeySwitch.App;

// Only two short, unmodified press/release pairs count; typing cancels the gesture.
internal sealed class ShiftTapDetector
{
    private int held;
    private long pressedAt, firstReleasedAt = -1;
    private bool clean;
    internal void Reset() { held = 0; clean = false; firstReleasedAt = -1; }
    internal bool Update(int key, bool down, long now, bool modifiers)
    {
        bool shift = key is 0x10 or 0xA0 or 0xA1;
        if (!shift || modifiers) { Reset(); return false; }
        if (down)
        {
            if (held != 0) { clean = false; firstReleasedAt = -1; return false; }
            held = key; pressedAt = now; clean = true;
            return false;
        }
        bool tap = held == key && clean && now - pressedAt is >= 0 and <= 250;
        held = 0; clean = false;
        if (!tap) { firstReleasedAt = -1; return false; }
        if (firstReleasedAt >= 0 && now - firstReleasedAt is >= 0 and <= 400)
        {
            firstReleasedAt = -1;
            return true;
        }
        firstReleasedAt = now;
        return false;
    }
}
