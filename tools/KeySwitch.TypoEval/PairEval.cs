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
                    if (clean) { counts.FalseCorrections++; bucket[2]++; counts.AddExample(counts.FalseExamples, $"{word} -> {decision.Corrected}"); }
                    else if (Normalize(decision.Corrected) == Normalize(expected)) { counts.Corrected++; bucket[0]++; }
                    else { counts.WrongCorrections++; bucket[1]++; counts.AddExample(counts.WrongExamples, $"{word} -> {decision.Corrected} (expected {expected})"); }
                }
                else if (!clean && !ignored) counts.AddExample(counts.MissExamples, $"{word} -> {expected} ({decision.Reason})");
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
    public List<string> FalseExamples = new(), WrongExamples = new(), MissExamples = new();
    // Per edit distance of the change: [corrected, wrong, false on clean].
    public SortedDictionary<int, int[]> ByDistance = new();
    public int[] Bucket(int distance) => ByDistance.TryGetValue(distance, out var b) ? b : (ByDistance[distance] = new int[3]);
    public void AddExample(List<string> list, string example) { if (list.Count < 40) list.Add(example); }
    public void Add(PairCounts other)
    {
        Sentences += other.Sentences; UnalignedWords += other.UnalignedWords; CleanWords += other.CleanWords;
        IgnoredEdits += other.IgnoredEdits; Typos += other.Typos; Corrected += other.Corrected;
        WrongCorrections += other.WrongCorrections; FalseCorrections += other.FalseCorrections;
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
            ByDistance = ByDistance.ToDictionary(x => x.Key.ToString(), x => new { Corrected = x.Value[0], Wrong = x.Value[1], FalseOnClean = x.Value[2] }),
            FalseExamples, WrongExamples, MissExamples,
        };
    }
}
