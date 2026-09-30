using KeySwitch.App;
using Xunit;
namespace KeySwitch.Core.Tests;

public sealed class ShiftTapTests
{
    [Fact]
    public void TwoCleanTapsTriggerOnceAndAllowRepeatedUndoGesture()
    {
        var detector = new ShiftTapDetector();
        for (int start = 0; start < 1000; start += 500)
        {
            Assert.False(detector.Update(0xA0, true, start, false));
            Assert.False(detector.Update(0xA0, false, start + 60, false));
            Assert.False(detector.Update(0xA0, true, start + 120, false));
            Assert.True(detector.Update(0xA0, false, start + 180, false));
        }
    }
    [Fact]
    public void ShiftTypingAndModifiersCancelGesture()
    {
        var detector = new ShiftTapDetector();
        detector.Update(0xA0, true, 0, false);
        detector.Update(0x41, true, 30, false);
        Assert.False(detector.Update(0xA0, false, 60, false));
        detector.Update(0xA0, true, 90, false);
        Assert.False(detector.Update(0xA0, false, 120, false));
        detector.Update(0xA0, true, 180, true);
        Assert.False(detector.Update(0xA0, false, 210, false));
    }
    [Fact]
    public void LongHoldRepeatTimeoutAndResetCannotTrigger()
    {
        var detector = new ShiftTapDetector();
        detector.Update(0xA0, true, 0, false);
        Assert.False(detector.Update(0xA0, false, 300, false));
        detector.Update(0xA0, true, 320, false);
        Assert.False(detector.Update(0xA0, false, 350, false));
        detector.Update(0xA0, true, 370, false);
        detector.Update(0xA0, true, 380, false);
        Assert.False(detector.Update(0xA0, false, 400, false));
        detector.Update(0xA0, true, 450, false);
        Assert.False(detector.Update(0xA0, false, 500, false));
        detector.Update(0xA0, true, 1000, false);
        Assert.False(detector.Update(0xA0, false, 1050, false));
        detector.Reset();
        detector.Update(0xA0, true, 1100, false);
        Assert.False(detector.Update(0xA0, false, 1150, false));
    }
}
