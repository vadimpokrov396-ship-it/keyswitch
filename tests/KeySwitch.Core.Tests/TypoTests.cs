using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Core.Tests;

public sealed class TypoTests
{
    public TypoTests() { TypoCorrector.RussianEnabled = true; }

    [Fact]
    public void CorrectsRussianExampleAfterLayoutDecision()
    {
        var boundary = new BoundaryEngine(new DecisionEngine());
        // An unambiguous typo: one candidate, no rival form. Borderline ones (превет) are left to Pause.
        var result = boundary.Complete("правительсво", " ");
        Assert.Equal("правительство", result.Replacement);
        Assert.Equal("typo-autocorrect", result.Reason);
    }

    // The Windows selftest (KeySwitch.exe --selftest) relies on exactly this correction.
    [Fact]
    public void CorrectsEnglishSelftestTypo()
    {
        var result = new BoundaryEngine(new DecisionEngine()).Complete("becuase", " ");
        Assert.Equal("because", result.Replacement);
        Assert.Equal("typo-autocorrect", result.Reason);
    }

    [Fact]
    public void ShortWordsAreNeverTypoCorrected()
    {
        Assert.False(new BoundaryEngine(new DecisionEngine()).Complete("teh", " ").Changed);
    }

    [Theory]
    [InlineData("привет")]
    [InlineData("hello")]
    [InlineData("Превет")]
    [InlineData("ПРЕВЕТ")]
    [InlineData("превет42")]
    [InlineData("some_name")]
    [InlineData("example.com")]
    public void ProtectedWordsStayUnchanged(string word)
    {
        var boundary = new BoundaryEngine(new DecisionEngine()) { LayoutEnabled = false };
        Assert.False(boundary.Complete(word, " ").Changed);
    }

    [Fact]
    public void ToggleAndExceptionAreIndependent()
    {
        var boundary = new BoundaryEngine(new DecisionEngine()) { LayoutEnabled = false };
        Assert.Equal("правительство", boundary.Complete("правительсво", " ").Replacement);
        Assert.Equal("правительсво", boundary.Complete("правительсво", " ", ["правительсво"]).Replacement);
        boundary.TypoEnabled = false;
        Assert.Equal("правительсво", boundary.Complete("правительсво", " ").Replacement);
        boundary.LayoutEnabled = true;
        Assert.Equal("привет", boundary.Complete("ghbdtn", " ").Replacement);
    }
}
