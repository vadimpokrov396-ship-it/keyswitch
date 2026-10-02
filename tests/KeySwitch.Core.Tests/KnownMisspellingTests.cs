using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Core.Tests;

// data/ru-known-misspellings.txt: owner-reviewed misspellings that data/ru.txt makes look like known words.
// TypoCorrector.RussianEnabled is static: tests that switch it must not run in parallel.
[Collection(RussianSwitch.Name)]
public sealed class KnownMisspellingTests
{
    private static readonly string DataFile = Path.Combine(AppContext.BaseDirectory, "fixtures", "ru-known-misspellings.txt");

    public static IEnumerable<object[]> Entries() => File.ReadLines(DataFile)
        .Where(line => !line.TrimStart().StartsWith('#'))
        .Select(line => line.Split('→', 2, StringSplitOptions.TrimEntries))
        .Where(parts => parts.Length == 2)
        .Select(parts => new object[] { parts[0], parts[1] });

    private static TypoDecision Auto(string word)
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = true;
        try { return new TypoCorrector(new DecisionEngine()).Evaluate(word); }
        finally { TypoCorrector.RussianEnabled = saved; }
    }

    [Fact]
    public void ListIsNotEmpty() => Assert.True(Entries().Count() >= 70);

    // Every correction is a real word and never itself a listed misspelling.
    [Theory]
    [MemberData(nameof(Entries))]
    public void CorrectionsAreKnownWords(string misspelling, string correction)
    {
        Assert.NotEqual(misspelling, correction);
        Assert.True(new DecisionEngine().IsKnownWord(correction, true), correction);
        Assert.DoesNotContain(Entries(), e => (string)e[0] == correction);
    }

    [Theory]
    [InlineData("обьяснить", "объяснить")]
    [InlineData("кажеться", "кажется")]
    [InlineData("колличество", "количество")]
    [InlineData("зделать", "сделать")]
    [InlineData("расчитывать", "рассчитывать")]
    [InlineData("помошник", "помощник")]
    public void AutoCorrects(string typed, string intended)
    {
        var decision = Auto(typed);
        Assert.True(decision.ShouldCorrect, decision.Reason);
        Assert.Equal(intended, decision.Corrected);
        Assert.Equal("typo-autocorrect-known-misspelling", decision.Reason);
    }

    // Words the owner marked "keep" stay known words.
    [Theory]
    [InlineData("светка")]
    [InlineData("мусорка")]
    [InlineData("персоналка")]
    public void KeptWordsUnchanged(string word) => Assert.False(Auto(word).ShouldCorrect);

    [Fact]
    public void PauseFixesSpellingWithoutLayoutSwitch()
    {
        var fix = new BoundaryEngine(new DecisionEngine()).Manual("Обьяснить");
        Assert.Equal("Объяснить", fix.Replacement);
        Assert.False(fix.LayoutChange);
    }

    [Fact]
    public void NotWithRussianAutoCorrectionOff()
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = false;
        try { Assert.False(new TypoCorrector(new DecisionEngine()).Evaluate("обьяснить").ShouldCorrect); }
        finally { TypoCorrector.RussianEnabled = saved; }
    }
}
