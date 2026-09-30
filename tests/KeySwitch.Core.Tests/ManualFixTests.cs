using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Core.Tests;

// Pause / double Shift: layout conversion when it yields a known word, otherwise a spelling fix, otherwise layout.
// TypoCorrector.RussianEnabled is static: tests that switch it must not run in parallel.
[Collection(RussianSwitch.Name)]
public sealed class ManualFixTests
{
    private static readonly BoundaryEngine Boundary = new(new DecisionEngine());

    [Fact]
    public void WrongLayoutWordIsConverted()
    {
        var fix = Boundary.Manual("ghbdtn");
        Assert.Equal("привет", fix.Replacement);
        Assert.True(fix.LayoutChange);
    }

    [Fact]
    public void RussianMisspellingIsFixedEvenWithRussianAutoCorrectionOff()
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = false;
        try
        {
            var fix = Boundary.Manual("правительсво");
            Assert.Equal("правительство", fix.Replacement);
            Assert.False(fix.LayoutChange);
            Assert.Equal("typo-manual", fix.Reason);
            Assert.Equal("Правительство", Boundary.Manual("Правительсво").Replacement);
        }
        finally { TypoCorrector.RussianEnabled = saved; }
    }

    [Fact]
    public void EnglishMisspellingIsFixed()
    {
        var fix = Boundary.Manual("becuase");
        Assert.Equal("because", fix.Replacement);
        Assert.False(fix.LayoutChange);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("привет")]
    public void CorrectWordsKeepTheOldLayoutConversion(string word)
    {
        var fix = Boundary.Manual(word);
        Assert.Equal(LayoutMap.Convert(word), fix.Replacement);
        Assert.True(fix.LayoutChange);
    }
}
