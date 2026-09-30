using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Core.Tests;

// Regression set "owner_typos" (fixtures/owner_typos.txt): real typos from the owner's messages.
public sealed class OwnerTypoTests
{
    public static IEnumerable<object[]> Pairs() => File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "owner_typos.txt"))
        .Where(line => !line.StartsWith('#') && line.Contains('\t'))
        .Select(line => line.Split('\t'))
        .Select(parts => new object[] { parts[0], parts[1] });

    // Precision first: whatever a mode does with these words, it never produces a wrong word.
    [Theory]
    [MemberData(nameof(Pairs))]
    public void NeverChangedIntoAWrongWord(string typo, string intended)
    {
        bool saved = TypoCorrector.RussianEnabled;
        TypoCorrector.RussianEnabled = true;
        try
        {
            var corrector = new TypoCorrector(new DecisionEngine());
            foreach (var decision in new[] { corrector.Evaluate(typo), corrector.Suggest(typo) })
                Assert.True(!decision.ShouldCorrect || decision.Corrected == intended, $"{typo} -> {decision.Corrected}, expected {intended} or no change");
        }
        finally { TypoCorrector.RussianEnabled = saved; }
    }
}
