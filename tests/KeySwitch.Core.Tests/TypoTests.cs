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
        var result = boundary.Complete("превет", " ");
        Assert.Equal("привет", result.Replacement);
        Assert.Equal("typo-autocorrect", result.Reason);
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
        Assert.Equal("привет", boundary.Complete("превет", " ").Replacement);
        Assert.Equal("превет", boundary.Complete("превет", " ", ["превет"]).Replacement);
        boundary.TypoEnabled = false;
        Assert.Equal("превет", boundary.Complete("превет", " ").Replacement);
        boundary.LayoutEnabled = true;
        Assert.Equal("привет", boundary.Complete("ghbdtn", " ").Replacement);
    }
}
