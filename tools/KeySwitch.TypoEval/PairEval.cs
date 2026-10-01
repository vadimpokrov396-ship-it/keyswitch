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
        // Threshold variants for tuning on the dev set, run through the same TypoCorrector code as the app.
        // Auto variants keep the 2.2 base lead and sweep the Russian lead offline from the recorded confidence;
        // manual variants (Pause) are scored on real typos only, since Pause is pressed on a misspelled word.
        var autoSweep = TypoPolicy.Auto with { RussianMinMargin = 0 };
        var manualSweep = TypoPolicy.Manual with { MinMargin = 0, RussianMinMargin = 0 };
        var variants = new List<Variant>
        {
            new("auto rank<=25k", autoSweep with { RussianRankCap = 25000 }, false),
            new("auto rank<=50k", autoSweep with { RussianRankCap = 50000 }, false),
            new("auto rank<=100k", autoSweep with { RussianRankCap = 100000 }, false),
            new("auto rank<=200k", autoSweep with { RussianRankCap = 200000 }, false),
            new("auto rank<=50k +2 edits for 8+ letters", autoSweep with { RussianDistance2MinLength = 8 }, false),
            new("auto without word pairs (1.3.0)", autoSweep with { ContextWeight = 0, RealWordMargin = double.PositiveInfinity }, false),
            new("auto word-pair context x0.5", autoSweep with { ContextWeight = 0.5 }, false),
            new("auto word-pair context x1", autoSweep with { ContextWeight = 1 }, false),
            new("auto word-pair context x2", autoSweep with { ContextWeight = 2 }, false),
            new("auto pair veto lift<-2", autoSweep with { ContextVeto = -2 }, false),
            new("auto pair veto lift<-3", autoSweep with { ContextVeto = -3 }, false),
            new("auto context x1 + veto lift<-2", autoSweep with { ContextWeight = 1, ContextVeto = -2 }, false),
            new("manual rank<=50k", manualSweep with { RussianRankCap = 50000 }, true),
            new("manual rank<=200k", manualSweep with { RussianRankCap = 200000 }, true),
            new("manual rank<=200k +2 edits for 8+ letters", manualSweep with { RussianRankCap = 200000, RussianDistance2MinLength = 8 }, true),
        };
        foreach (var input in inputs)
        {
            var counts = new PairCounts();
            foreach (var line in File.ReadLines(input))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var row = JsonDocument.Parse(line);
                string source = row.RootElement.GetProperty("source").GetString()!.Trim('﻿');
                string correction = row.RootElement.GetProperty("correction").GetString()!.Trim('﻿');
                Evaluate(corrector, source, correction, counts, variants);
            }
            report[Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(input))) + "/" + Path.GetFileName(input)] = counts.Summary();
            total.Add(counts);
        }
        report["total"] = total.Summary();
        report["variants"] = variants.ToDictionary(v => v.Name, v => v.Summary(total.Typos, total.CleanWords));
        // Real-word errors decided by the previous word, per minimum pair count, confusion kind and margin.
        report["realword"] = new
        {
            PairContext = TypoCorrector.HasPairContext,
            Probes = RealWordProbes.ToDictionary(p => $"pairs>={p.MinPairs}", p => p.Summary(total.Typos, total.CleanWords)),
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(output, json);
        Console.WriteLine(json);
    }

    private static readonly RealWordProbe[] RealWordProbes = [new(3), new(20), new(100)];

    private static void Evaluate(TypoCorrector corrector, string source, string correction, PairCounts counts, List<Variant> variants)
    {
        var typed = Word.Matches(source).Select(x => x.Value).ToArray();
        var fixedWords = Word.Matches(correction).Select(x => x.Value).ToArray();
        counts.Sentences++;
        var target = Align(typed, fixedWords);
        string? previous = null, previous2 = null;
        for (int i = 0; i < typed.Length; i++)
        {
            string word = typed[i];
            string? expected = target[i];
            if (expected is null) { counts.UnalignedWords++; }
            else
            {
                bool clean = word == expected;
                // Case-only and е/ё-only differences are not spelling errors KeySwitch should fix.
                bool ignored = !clean && Normalize(word) == Normalize(expected);
                var decision = corrector.Evaluate(word, previous, previous2);
                if (clean) counts.CleanWords++;
                else if (ignored) counts.IgnoredEdits++;
                else counts.Typos++;
                if (!ignored && decision.ShouldCorrect)
                {
                    int distance = Distance(word.ToLowerInvariant(), decision.Corrected.ToLowerInvariant());
                    var bucket = counts.Bucket(distance);
                    counts.Changes.Add((decision.Confidence, clean ? 2 : Normalize(decision.Corrected) == Normalize(expected) ? 0 : 1));
                    if (clean) { counts.FalseCorrections++; bucket[2]++; counts.AddExample(counts.FalseExamples, $"{word} -> {decision.Corrected}"); }
                    else if (Normalize(decision.Corrected) == Normalize(expected)) { counts.Corrected++; bucket[0]++; }
                    else { counts.WrongCorrections++; bucket[1]++; counts.AddExample(counts.WrongExamples, $"{word} -> {decision.Corrected} (expected {expected})"); }
                }
                else if (!clean && !ignored)
                {
                    counts.AddExample(counts.MissExamples, $"{word} -> {expected} ({decision.Reason})");
                    counts.MissReasons[decision.Reason] = counts.MissReasons.GetValueOrDefault(decision.Reason) + 1;
                    if (decision.Reason == "known-original")
                    {
                        string kind = TypoCorrector.Confusion(word, expected).ToString();
                        counts.RealWordTypos[kind] = counts.RealWordTypos.GetValueOrDefault(kind) + 1;
                        if (kind != "None") counts.AddExample(counts.RealWordExamples, $"{previous} {word} -> {expected} ({kind})");
                    }
                }
                if (!ignored && decision.Reason == "known-original")
                    foreach (var probe in RealWordProbes)
                    {
                        var result = corrector.Evaluate(word, previous, previous2, null, probe.Policy);
                        if (!result.ShouldCorrect) continue;
                        int outcome = clean ? 2 : Normalize(result.Corrected) == Normalize(expected) ? 0 : 1;
                        probe.Changes.Add((result.Reason[(result.Reason.LastIndexOf('-') + 1)..], result.Confidence, outcome));
                        if (probe.Examples.Count < 60 && outcome != 0)
                            probe.Examples.Add($"{previous} {word} -> {result.Corrected} ({(clean ? "was right" : "expected " + expected)}, {result.Confidence:0.0})");
                    }
                // Manual mode (Pause on a misspelled word, no context): would the suggestion be the intended word?
                if (!clean && !ignored && corrector.Suggest(word, null, null, null, TypoPolicy.Manual with { MinMargin = 0, RussianMinMargin = 0 }) is { ShouldCorrect: true } suggestion)
                    counts.Suggestions.Add((suggestion.Confidence, Normalize(suggestion.Corrected) == Normalize(expected)));
                if (!ignored)
                    foreach (var variant in variants)
                    {
                        if (variant.Manual && clean) continue;
                        var result = variant.Manual ? corrector.Suggest(word, null, null, null, variant.Policy)
                            : corrector.Evaluate(word, previous, previous2, null, variant.Policy);
                        if (result.ShouldCorrect)
                            variant.Changes.Add((result.Confidence, clean ? 2 : Normalize(result.Corrected) == Normalize(expected) ? 0 : 1));
                    }
            }
            previous2 = previous;
            previous = word.ToLowerInvariant();
        }
    }

    private static string Normalize(string word) => word.ToLowerInvariant().Replace('ё', 'е');

    /// <summary>Pairs typed words with corrected words: identical words via LCS, and the words between two
    /// anchors one to one when both gaps have the same length. Splits and merges stay unaligned (null).</summary>
    private static string?[] Align(string[] typed, string[] fixedWords)
    {
        int n = typed.Length, m = fixedWords.Length;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = typed[i] == fixedWords[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var result = new string?[n];
        int a = 0, b = 0, gapA = 0, gapB = 0;
        void CloseGap(int endA, int endB)
        {
            if (endA - gapA == endB - gapB) for (int k = 0; k < endA - gapA; k++) result[gapA + k] = fixedWords[gapB + k];
        }
        while (a < n && b < m)
        {
            if (typed[a] == fixedWords[b]) { CloseGap(a, b); result[a] = fixedWords[b]; a++; b++; gapA = a; gapB = b; }
            else if (lcs[a + 1, b] >= lcs[a, b + 1]) a++;
            else b++;
        }
        CloseGap(n, m);
        return result;
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }
}

sealed class PairCounts
{
    public int Sentences, UnalignedWords, CleanWords, IgnoredEdits, Typos, Corrected, WrongCorrections, FalseCorrections;
    public List<string> FalseExamples = new(), WrongExamples = new(), MissExamples = new(), RealWordExamples = new();
    // Why real typos were left unchanged, and how typos that are real words relate to the intended word.
    public SortedDictionary<string, int> MissReasons = new(StringComparer.Ordinal), RealWordTypos = new(StringComparer.Ordinal);
    // Manual suggestions for real typos: (confidence, suggestion == intended word).
    public List<(double Confidence, bool Right)> Suggestions = new();
    // Every automatic change: (confidence, outcome 0 = corrected, 1 = wrong, 2 = false on clean).
    public List<(double Confidence, int Outcome)> Changes = new();
    // Per edit distance of the change: [corrected, wrong, false on clean].
    public SortedDictionary<int, int[]> ByDistance = new();
    public int[] Bucket(int distance) => ByDistance.TryGetValue(distance, out var b) ? b : (ByDistance[distance] = new int[3]);
    public void AddExample(List<string> list, string example) { if (list.Count < 40) list.Add(example); }
    public void Add(PairCounts other)
    {
        Sentences += other.Sentences; UnalignedWords += other.UnalignedWords; CleanWords += other.CleanWords;
        IgnoredEdits += other.IgnoredEdits; Typos += other.Typos; Corrected += other.Corrected;
        WrongCorrections += other.WrongCorrections; FalseCorrections += other.FalseCorrections;
        Changes.AddRange(other.Changes);
        foreach (var (k, v) in other.MissReasons) MissReasons[k] = MissReasons.GetValueOrDefault(k) + v;
        foreach (var (k, v) in other.RealWordTypos) RealWordTypos[k] = RealWordTypos.GetValueOrDefault(k) + v;
        foreach (var example in other.RealWordExamples) AddExample(RealWordExamples, example);
        Suggestions.AddRange(other.Suggestions);
        foreach (var (distance, values) in other.ByDistance) { var b = Bucket(distance); for (int i = 0; i < 3; i++) b[i] += values[i]; }
    }
    public object Summary()
    {
        int changes = Corrected + WrongCorrections + FalseCorrections;
        return new
        {
            Sentences, UnalignedWords, CleanWords, Typos, IgnoredEdits, Corrected, WrongCorrections, FalseCorrections,
            // Share of automatic changes that were right; the number that matters for enabling RU by default.
            ChangePrecision = Math.Round((double)Corrected / Math.Max(1, changes), 4),
            Recall = Math.Round((double)Corrected / Math.Max(1, Typos), 4),
            FalseCorrectionRate = Math.Round((double)FalseCorrections / Math.Max(1, CleanWords), 5),
            // What precision/recall a stricter confidence threshold would give (changes below it are skipped).
            Sweep = new[] { 2.2, 2.6, 3.0, 3.5, 4.0, 4.5, 5.0, 6.0, 7.0 }.ToDictionary(t => t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), t =>
            {
                int c = Changes.Count(x => x.Confidence >= t && x.Outcome == 0), w = Changes.Count(x => x.Confidence >= t && x.Outcome == 1),
                    f = Changes.Count(x => x.Confidence >= t && x.Outcome == 2);
                return new { Precision = Math.Round((double)c / Math.Max(1, c + w + f), 4), Recall = Math.Round((double)c / Math.Max(1, Typos), 4),
                    FalseRate = Math.Round((double)f / Math.Max(1, CleanWords), 5) };
            }),
            // Pause on a misspelled word: share of suggestions that are right, and share of typos fixed that way.
            ManualSweep = new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0 }.ToDictionary(t => t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), t =>
            {
                int right = Suggestions.Count(x => x.Confidence >= t && x.Right), wrong = Suggestions.Count(x => x.Confidence >= t && !x.Right);
                return new { Accuracy = Math.Round((double)right / Math.Max(1, right + wrong), 4), Coverage = Math.Round((double)right / Math.Max(1, Typos), 4) };
            }),
            ByDistance = ByDistance.ToDictionary(x => x.Key.ToString(), x => new { Corrected = x.Value[0], Wrong = x.Value[1], FalseOnClean = x.Value[2] }),
            FalseExamples, WrongExamples, MissExamples, MissReasons, RealWordTypos, RealWordExamples,
        };
    }
}

sealed record Variant(string Name, TypoPolicy Policy, bool Manual)
{
    // (confidence, outcome 0 = intended word, 1 = wrong word, 2 = changed a correct word)
    public List<(double Confidence, int Outcome)> Changes { get; } = new();
    public object Summary(int typos, int cleanWords) =>
        (Manual ? new[] { 1.0, 2.0, 2.5, 3.0, 4.0 } : new[] { 2.2, 3.0, 4.0, 5.0 }).ToDictionary(
            t => t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), t =>
            {
                int right = Changes.Count(x => x.Confidence >= t && x.Outcome == 0), wrong = Changes.Count(x => x.Confidence >= t && x.Outcome == 1),
                    falseOnClean = Changes.Count(x => x.Confidence >= t && x.Outcome == 2);
                return new
                {
                    Right = right, Wrong = wrong, FalseOnClean = falseOnClean,
                    Precision = Math.Round((double)right / Math.Max(1, right + wrong + falseOnClean), 4),
                    Recall = Math.Round((double)right / Math.Max(1, typos), 4),
                    FalseRate = Math.Round((double)falseOnClean / Math.Max(1, cleanWords), 5),
                };
            });
}

/// <summary>Real-word corrections with every confusion kind and no margin, so the summary can show each kind and
/// threshold; only words the shipped auto mode keeps as known are probed.</summary>
sealed class RealWordProbe(double minPairs)
{
    public double MinPairs { get; } = minPairs;
    public TypoPolicy Policy { get; } = TypoPolicy.Auto with
    {
        RealWordMargin = 0, RealWordMinPairs = minPairs,
        RealWordKinds = RealWordKinds.Tsya | RealWordKinds.SecondPlural | RealWordKinds.OtherEdit,
    };
    // (kind, margin, outcome 0 = intended word, 1 = wrong word, 2 = changed a correct word)
    public List<(string Kind, double Margin, int Outcome)> Changes { get; } = new();
    public List<string> Examples { get; } = new();
    public object Summary(int typos, int cleanWords) => new
    {
        ByKind = Changes.Select(c => c.Kind).Distinct().OrderBy(k => k).ToDictionary(k => k, k =>
            new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0 }.ToDictionary(t => t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), t =>
            {
                var selected = Changes.Where(c => c.Kind == k && c.Margin >= t).ToList();
                int right = selected.Count(c => c.Outcome == 0), wrong = selected.Count(c => c.Outcome == 1), falseOnClean = selected.Count(c => c.Outcome == 2);
                return new { Right = right, Wrong = wrong, FalseOnClean = falseOnClean,
                    Precision = Math.Round((double)right / Math.Max(1, right + wrong + falseOnClean), 4),
                    FalseRate = Math.Round((double)falseOnClean / Math.Max(1, cleanWords), 5) };
            })),
        Examples,
    };
}
