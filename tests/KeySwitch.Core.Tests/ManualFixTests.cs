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

// Pause pressed again cycles through further spellings (T9-like), then the app restores the typed word.
public sealed class ManualOptionsTests
{
    private static IReadOnlyList<ManualFix> Options(string token) => new BoundaryEngine(new DecisionEngine()).ManualOptions(token);

    [Fact]
    public void FirstOptionIsWhatPauseDidBefore()
    {
        var engine = new BoundaryEngine(new DecisionEngine());
        foreach (var token in new[] { "ghbdtn", "правительсво", "превет", "привет", "becuase" })
            Assert.Equal(engine.Manual(token), engine.ManualOptions(token)[0]);
    }

    [Theory]
    [InlineData("превет", "привет")]
    [InlineData("Превет", "Привет")]
    public void AmbiguousTypoOffersTheIntendedSpelling(string typed, string intended) =>
        Assert.Contains(intended, Options(typed).Select(o => o.Replacement));

    [Theory]
    [InlineData("правительсво")]
    [InlineData("превет")]
    [InlineData("becuase")]
    public void OptionsAreDistinctAndAtMostThree(string typed)
    {
        var options = Options(typed).Select(o => o.Replacement).ToList();
        Assert.InRange(options.Count, 1, 3);
        Assert.Equal(options.Count, options.Distinct().Count());
        Assert.DoesNotContain(typed, options);
        Assert.All(Options(typed).Skip(1), o => Assert.False(o.LayoutChange));
    }

    // Known words and wrong-layout words that form a known word get no spelling alternatives.
    [Theory]
    [InlineData("привет")]
    [InlineData("ghbdtn")]
    public void NoAlternativesForKnownWords(string typed) => Assert.Single(Options(typed));
}
