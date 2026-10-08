using KeySwitch.Core;

/// <summary>Regression set "owner_typos": prints what each mode does with the owner's real typos as a Markdown table.
/// A line may carry the words before the typo as context: "много стаей → много статей".</summary>
static class OwnerEval
{
    public static void Run(string path)
    {
        TypoCorrector.RussianEnabled = true;
        var corrector = new TypoCorrector(new DecisionEngine());
        var modes = new (string Name, Func<string, string?, string?, TypoDecision> Decide)[]
        {
            ("auto", (w, p, p2) => corrector.Evaluate(w, p, p2)),
            ("Pause", (w, _, _) => corrector.Suggest(w)),
            ("auto, real words: any kind, lead >= 3", (w, p, p2) => corrector.Evaluate(w, p, p2, null, TypoPolicy.Auto with
                { RealWordMargin = 3, RealWordKinds = RealWordKinds.Tsya | RealWordKinds.SecondPlural | RealWordKinds.OtherEdit })),
            ("auto +2 edits (8+ letters)", (w, p, p2) => corrector.Evaluate(w, p, p2, null, TypoPolicy.Auto with { RussianDistance2MinLength = 8 })),
        };
        Console.WriteLine("| typo | intended | " + string.Join(" | ", modes.Select(m => m.Name)) + " |");
        Console.WriteLine("|---|---|" + string.Concat(modes.Select(_ => "---|")));
        foreach (var line in File.ReadLines(path))
        {
            if (line.TrimStart().StartsWith('#')) continue;
            var parts = line.Split(new[] { "→", "->", "\t" }, 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
            var typed = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string intended = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
            string? previous = typed.Length > 1 ? typed[^2].ToLowerInvariant() : null, previous2 = typed.Length > 2 ? typed[^3].ToLowerInvariant() : null;
            var cells = modes.Select(m =>
            {
                var d = m.Decide(typed[^1], previous, previous2);
                if (!d.ShouldCorrect) return $"— ({d.Reason})";
                return (d.Corrected == intended ? "✅ " : "❌ ") + $"{d.Corrected} ({d.Confidence:0.0})";
            });
            Console.WriteLine($"| {parts[0]} | {parts[1]} | " + string.Join(" | ", cells) + " |");
        }
    }
}
