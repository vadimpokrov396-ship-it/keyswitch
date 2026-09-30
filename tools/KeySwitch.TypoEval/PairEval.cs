using System.Text.Json;
using System.Text.RegularExpressions;
using KeySwitch.Core;

/// <summary>Russian typo correction measured on real misspellings: JSONL files with "source" (as typed) and
/// "correction" fields, e.g. the ai-forever/spellcheck_benchmark sets (RUSpellRU, MultidomainGold, ...).</summary>
static class PairEval
{
    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled);

    public static void Run(string output, IEnumerable<string> inputs)
    {
        TypoCorrector.RussianEnabled = true;
        var corrector = new TypoCorrector(new DecisionEngine());
        var report = new Dictionary<string, object>();
        var total = new PairCounts();
        foreach (var input in inputs)
        {
            var counts = new PairCounts();
            foreach (var line in File.ReadLines(input))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var row = JsonDocument.Parse(line);
                string source = row.RootElement.GetProperty("source").GetString()!.Trim('﻿');
                string correction = row.RootElement.GetProperty("correction").GetString()!.Trim('﻿');
                Evaluate(corrector, source, correction, counts);
            }
            report[Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(input))) + "/" + Path.GetFileName(input)] = counts.Summary();
            total.Add(counts);
        }
        report["total"] = total.Summary();
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(output, json);
        Console.WriteLine(json);
    }

    private static void Evaluate(TypoCorrector corrector, string source, string correction, PairCounts counts)
    {
        var typed = Word.Matches(source).Select(x => x.Value).ToArray();
        var fixedWords = Word.Matches(correction).Select(x => x.Value).ToArray();
        counts.Sentences++;
        // Word splits/merges cannot be aligned position by position; they are not single-word typos anyway.
        if (typed.Length != fixedWords.Length) { counts.SkippedSentences++; return; }
        string? previous = null, previous2 = null;
        for (int i = 0; i < typed.Length; i++)
        {
            string word = typed[i], target = fixedWords[i];
            var decision = corrector.Evaluate(word, previous, previous2);
            if (word == target) counts.CleanWords++;
            // Case-only and е/ё-only differences are not spelling errors KeySwitch should fix.
            else if (string.Equals(word, target, StringComparison.OrdinalIgnoreCase) ||
                     word.Replace('ё', 'е') == target.Replace('ё', 'е')) { counts.IgnoredEdits++; decision = null; }
            else counts.Typos++;
            if (decision is { ShouldCorrect: true })
            {
                if (word == target) { counts.FalseCorrections++; counts.AddExample(counts.FalseExamples, $"{word} -> {decision.Corrected}"); }
                else if (decision.Corrected == target) counts.Corrected++;
                else { counts.WrongCorrections++; counts.AddExample(counts.WrongExamples, $"{word} -> {decision.Corrected} (expected {target})"); }
            }
            else if (word != target && decision is not null) counts.AddExample(counts.MissExamples, $"{word} -> {target} ({decision.Reason})");
            previous2 = previous;
            previous = word.ToLowerInvariant();
        }
    }
}

sealed class PairCounts
{
    public int Sentences, SkippedSentences, CleanWords, IgnoredEdits, Typos, Corrected, WrongCorrections, FalseCorrections;
    public List<string> FalseExamples = new(), WrongExamples = new(), MissExamples = new();
    public void AddExample(List<string> list, string example) { if (list.Count < 40) list.Add(example); }
    public void Add(PairCounts other)
    {
        Sentences += other.Sentences; SkippedSentences += other.SkippedSentences; CleanWords += other.CleanWords;
        IgnoredEdits += other.IgnoredEdits; Typos += other.Typos; Corrected += other.Corrected;
        WrongCorrections += other.WrongCorrections; FalseCorrections += other.FalseCorrections;
    }
    public object Summary()
    {
        int changes = Corrected + WrongCorrections + FalseCorrections;
        return new
        {
            Sentences, SkippedSentences, CleanWords, Typos, IgnoredEdits, Corrected, WrongCorrections, FalseCorrections,
            // Share of automatic changes that were right; the number that matters for enabling RU by default.
            ChangePrecision = Math.Round((double)Corrected / Math.Max(1, changes), 4),
            Recall = Math.Round((double)Corrected / Math.Max(1, Typos), 4),
            FalseCorrectionRate = Math.Round((double)FalseCorrections / Math.Max(1, CleanWords), 5),
            FalseExamples, WrongExamples, MissExamples,
        };
    }
}
