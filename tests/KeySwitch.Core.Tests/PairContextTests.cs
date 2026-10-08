using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Core.Tests;

// TypoCorrector.RussianEnabled is static: tests that switch it must not run in parallel.
[Collection(RussianSwitch.Name)]
public sealed class PairContextTests
{
    [Theory]
    [InlineData("учится", "учиться", RealWordKinds.Tsya)]
    [InlineData("учиться", "учится", RealWordKinds.Tsya)]
    [InlineData("напишете", "напишите", RealWordKinds.SecondPlural)]
    [InlineData("сделаите", "сделаете", RealWordKinds.SecondPlural)]
    [InlineData("стаей", "статей", RealWordKinds.OtherEdit)]
    [InlineData("Учится", "учиться", RealWordKinds.Tsya)]
    [InlineData("статей", "статей", RealWordKinds.None)]
    [InlineData("стаями", "статей", RealWordKinds.None)]
    public void ConfusionKinds(string typed, string intended, RealWordKinds kind) =>
        Assert.Equal(kind, TypoCorrector.Confusion(typed, intended));

    [Fact]
    public void PairTableIsBundled() => Assert.True(TypoCorrector.HasPairContext);

    private static TypoDecision Auto(string word, string? previous)
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = true;
        try { return new TypoCorrector(new DecisionEngine()).Evaluate(word, previous); }
        finally { TypoCorrector.RussianEnabled = saved; }
    }

    // тся/ться decided by the previous word.
    [Theory]
    [InlineData("мне", "нравиться", "нравится")]
    [InlineData("он", "находиться", "находится")]
    [InlineData("может", "вернутся", "вернуться")]
    [InlineData("чтобы", "убедится", "убедиться")]
    [InlineData("Мне", "нравиться", "нравится")]
    public void TsyaFixedByPreviousWord(string previous, string typed, string intended)
    {
        var decision = Auto(typed, previous);
        Assert.True(decision.ShouldCorrect, decision.Reason);
        Assert.Equal(intended, decision.Corrected);
        Assert.Equal("typo-autocorrect-context-tsya", decision.Reason);
    }

    // Correct forms, no or unknown context, and other real-word confusions stay as typed.
    [Theory]
    [InlineData("мне", "нравится")]
    [InlineData("он", "находится")]
    [InlineData("может", "вернуться")]
    [InlineData(null, "нравиться")]
    [InlineData("qwerty", "нравиться")]
    [InlineData("много", "стаей")]
    public void KnownWordsKept(string? previous, string typed) => Assert.False(Auto(typed, previous).ShouldCorrect);

    [Fact]
    public void NotWithRussianAutoCorrectionOff()
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = false;
        try { Assert.False(new TypoCorrector(new DecisionEngine()).Evaluate("нравиться", "мне").ShouldCorrect); }
        finally { TypoCorrector.RussianEnabled = saved; }
    }

    [Fact]
    public void BoundaryEngineUsesThePreviousWord()
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = true;
        try
        {
            var boundary = new BoundaryEngine(new DecisionEngine());
            Assert.False(boundary.Complete("мне", " ").Changed);
            var result = boundary.Complete("нравиться", " ");
            Assert.True(result.Changed);
            Assert.Equal("нравится", result.Replacement);
        }
        finally { TypoCorrector.RussianEnabled = saved; }
    }
}

// Capitalized words at a sentence start (TypoPolicy.SentenceStartCapitals).
[Collection(RussianSwitch.Name)]
public sealed class SentenceStartCapitalTests
{
    private static TypoDecision Evaluate(string word, bool sentenceStart, TypoPolicy policy)
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = true;
        try { return new TypoCorrector(new DecisionEngine()).Evaluate(word, null, null, null, policy, sentenceStart); }
        finally { TypoCorrector.RussianEnabled = saved; }
    }

    private static readonly TypoPolicy Capitals = TypoPolicy.Auto with { SentenceStartCapitals = true, CapitalMinMargin = 4 };

    [Fact]
    public void CapitalAtSentenceStartIsFixedAndRecapitalized()
    {
        var decision = Evaluate("Правительсво", true, Capitals);
        Assert.True(decision.ShouldCorrect, decision.Reason);
        Assert.Equal("Правительство", decision.Corrected);
        Assert.Equal("Правительсво", decision.Original);
    }

    [Fact]
    public void CapitalInsideSentenceStaysProtected() => Assert.Equal("protected-case", Evaluate("Правительсво", false, Capitals).Reason);

    [Fact]
    public void KnownNamesStayAtSentenceStart() => Assert.False(Evaluate("Светка", true, Capitals).ShouldCorrect);

    [Fact]
    public void AllCapsStayProtected() => Assert.False(Evaluate("ПРАВИТЕЛЬСВО", true, Capitals).ShouldCorrect);

    [Fact]
    public void AutoPolicyChecksSentenceStarts() => Assert.True(TypoPolicy.Auto.SentenceStartCapitals);

    // The app path: an empty context (start of input or after . ! ?) is a sentence start; elsewhere capitals stay.
    [Fact]
    public void BoundaryEngineFixesCapitalOnlyAtSentenceStart()
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = true;
        try
        {
            var boundary = new BoundaryEngine(new DecisionEngine());
            Assert.Equal("Правительство", boundary.Complete("Правительсво", " ").Replacement);
            Assert.Equal("Правительсво", boundary.Complete("Правительсво", " ").Replacement);
            Assert.Equal("правительство.", boundary.Complete("правительсво.", " ").Replacement);
            Assert.Equal("Правительство", boundary.Complete("Правительсво", " ").Replacement);
        }
        finally { TypoCorrector.RussianEnabled = saved; }
    }
}
