using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Core.Tests;

public sealed class BoundaryTests
{
    [Fact]
    public void TrailingPunctuationRemainsPunctuationWhileInternalLayoutPunctuationConverts()
    {
        var engine = new DecisionEngine();
        Assert.Equal("привет,", new BoundaryEngine(engine).Complete("ghbdtn,", " ").Replacement);
        Assert.Equal("спасибо", new BoundaryEngine(engine).Complete("cgfcb,j", " ").Replacement);
        Assert.Equal("hello,", new BoundaryEngine(engine).Complete("hello,", " ").Replacement);
        Assert.Equal("компьютер", new BoundaryEngine(engine).Complete("rjvgm.nth", " ").Replacement);
    }

    [Fact]
    public void ShortWordCanBeRepairedByFollowingLongWord()
    {
        var boundary = new BoundaryEngine(new DecisionEngine());
        Assert.False(boundary.Complete("yt", " ").Changed);
        var edit = boundary.Complete("ghbdtn", " ");
        Assert.True(edit.Changed);
        Assert.Equal("не привет", edit.Replacement);
        Assert.Equal(3, edit.PreviousCharacters);
    }

    [Fact]
    public void DoubleSpaceAndScriptChangeCannotTriggerRetroactiveCorrection()
    {
        var boundary = new BoundaryEngine(new DecisionEngine());
        boundary.Complete("yt", " ");
        boundary.Complete("", " ");
        var edit = boundary.Complete("ghbdtn", " ");
        Assert.Equal("привет", edit.Replacement);
        Assert.Equal(0, edit.PreviousCharacters);
    }

    [Fact]
    public void FullTokenExceptionAndDomainAreProtected()
    {
        var boundary = new BoundaryEngine(new DecisionEngine());
        Assert.False(boundary.Complete("ghbdtn.", " ", ["ghbdtn."]).Changed);
        Assert.False(boundary.Complete("ghbdtn.org", " ").Changed);
        Assert.False(boundary.Complete("руддщ.сщь", " ").Changed);
    }
}
