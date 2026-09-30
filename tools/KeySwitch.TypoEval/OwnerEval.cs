using KeySwitch.Core;

/// <summary>Regression set "owner_typos": prints what each mode does with the owner's real typos as a Markdown table.</summary>
static class OwnerEval
{
    public static void Run(string path)
    {
        TypoCorrector.RussianEnabled = true;
        var corrector = new TypoCorrector(new DecisionEngine());
        var modes = new (string Name, Func<string, TypoDecision> Decide)[]
        {
            ("auto", w => corrector.Evaluate(w)),
            ("Pause", w => corrector.Suggest(w)),
            ("auto +2 edits (8+ letters)", w => corrector.Evaluate(w, null, null, null, TypoPolicy.Auto with { RussianDistance2MinLength = 8 })),
            ("Pause +2 edits (8+ letters)", w => corrector.Suggest(w, null, null, null, TypoPolicy.Manual with { RussianDistance2MinLength = 8 })),
        };
        Console.WriteLine("| typo | intended | " + string.Join(" | ", modes.Select(m => m.Name)) + " |");
        Console.WriteLine("|---|---|" + string.Concat(modes.Select(_ => "---|")));
        foreach (var line in File.ReadLines(path))
        {
            if (line.TrimStart().StartsWith('#')) continue;
            var parts = line.Split(new[] { "→", "->", "\t" }, 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
            var cells = modes.Select(m =>
            {
                var d = m.Decide(parts[0]);
                if (!d.ShouldCorrect) return $"— ({d.Reason})";
                return (d.Corrected == parts[1] ? "✅ " : "❌ ") + $"{d.Corrected} ({d.Confidence:0.0})";
            });
            Console.WriteLine($"| {parts[0]} | {parts[1]} | " + string.Join(" | ", cells) + " |");
        }
    }
}
