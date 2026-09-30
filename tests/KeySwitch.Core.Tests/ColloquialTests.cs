using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Core.Tests;

// Colloquial / chat spellings (data/ru-colloquial.txt) are never turned into "correct" words, in either mode.
[Collection(RussianSwitch.Name)]
public sealed class ColloquialTests
{
    [Theory]
    [InlineData("ваще")]
    [InlineData("щас")]
    [InlineData("чё")]
    [InlineData("норм")]
    [InlineData("спс")]
    [InlineData("спасибки")]
    [InlineData("кароч")]
    [InlineData("седня")]
    [InlineData("Ваще")]
    public void NeverCorrected(string word)
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = true;
        try
        {
            var corrector = new TypoCorrector(new DecisionEngine());
            Assert.False(corrector.Evaluate(word).ShouldCorrect);
            Assert.False(corrector.Suggest(word).ShouldCorrect);
        }
        finally { TypoCorrector.RussianEnabled = saved; }
    }
}
