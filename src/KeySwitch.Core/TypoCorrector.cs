using System.Reflection;

namespace KeySwitch.Core;

public sealed record TypoDecision(bool ShouldCorrect, string Original, string Corrected, double Confidence, string Reason);

/// <summary>Offline spelling suggestions from ranked embedded word lists. The Bloom filters veto known forms.</summary>
public sealed class TypoCorrector
{
    /// <summary>Russian typo autocorrect is off by default in 1.2: measured precision 66% (EN 97.6%) is too low to
    /// change words automatically. Enable via settings (RussianTypoEnabled) once the RU model improves.</summary>
    public static bool RussianEnabled { get; set; }

    private sealed record Entry(string Word, int Rank);
    private sealed class LanguageData
    {
        // Keyed with ё spelled е, as users usually type it.
        public readonly Dictionary<string, Entry> Words = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<Entry>> Deletes = new(StringComparer.Ordinal);
        public bool Ranked(string word) => Words.ContainsKey(Plain(word));
        public Entry? Find(string word) => Words.GetValueOrDefault(Plain(word));
        private Dictionary<int, List<Entry>>? byLength;
        public IReadOnlyList<Entry> OfLength(int length)
        {
            byLength ??= Words.Values.GroupBy(x => x.Word.Length).ToDictionary(g => g.Key, g => g.ToList());
            return byLength.TryGetValue(length, out var entries) ? entries : Array.Empty<Entry>();
        }
        public readonly Dictionary<string, double> Trigrams = new(StringComparer.Ordinal);
        public double TotalTrigrams;
        public readonly int Alphabet;
        public LanguageData(string resource, int alphabet, bool deleteIndex = true)
        {
            Alphabet = alphabet;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing {resource}");
            using var reader = new StreamReader(stream);
            int rank = 0;
            while (reader.ReadLine() is { } line)
            {
                var word = line.Trim().ToLowerInvariant();
                if (word.Length < 2 || word.Length > 32 || !word.All(char.IsLetter) || Words.ContainsKey(Plain(word))) continue;
                var entry = new Entry(word, ++rank);
                Words.Add(Plain(word), entry);
                // Full one-edit vocabulary; the two-edit index is limited to frequent words.
                if (deleteIndex) foreach (var deleted in DeletesOf(word, rank <= 15000 ? 2 : 1))
                {
                    if (!Deletes.TryGetValue(deleted, out var entries)) Deletes[deleted] = entries = new();
                    entries.Add(entry);
                }
                double weight = 1 / Math.Sqrt(rank);
                string padded = "^" + word + "$";
                for (int i = 0; i + 3 <= padded.Length; i++)
                {
                    string gram = padded.Substring(i, 3);
                    Trigrams[gram] = Trigrams.GetValueOrDefault(gram) + weight;
                    TotalTrigrams += weight;
                }
            }
        }
        public double LanguageScore(string word)
        {
            string padded = "^" + word + "$";
            double score = 0;
            for (int i = 0; i + 3 <= padded.Length; i++)
                score += Math.Log((Trigrams.GetValueOrDefault(padded.Substring(i, 3)) + 0.05) /
                                  (TotalTrigrams + 0.05 * Alphabet * Alphabet * Alphabet));
            return score / Math.Max(1, padded.Length - 2);
        }
    }

    private static readonly Lazy<LanguageData> En = new(() => new("KeySwitch.en.txt", 26));
    // Russian candidates come from a list of frequent word FORMS (tools/build_ru_typo_list.py) when bundled;
    // the lemma-heavy ru.txt is the fallback. Russian uses single edits looked up directly, no delete index.
    private static readonly bool RussianFormList = Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("KeySwitch.ru-typo.txt");
    private static readonly Lazy<LanguageData> Ru = new(() => new(
        RussianFormList ? "KeySwitch.ru-typo.txt" : "KeySwitch.ru.txt", 33, deleteIndex: false));
    private static string Plain(string word) => word.Replace('ё', 'е');
    // Lowercase forms attested in edited corpora but absent from OpenCorpora (loanwords, slang, diminutives).
    private static readonly Lazy<HashSet<string>> RussianSeen = new(() =>
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KeySwitch.ru-seen.txt");
        if (stream is null) return words;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) if (line.Length > 0) words.Add(Plain(line.Trim()));
        return words;
    });
    // Colloquial / chat spellings (data/ru-colloquial.txt): never turned into "correct" words (ваще, щас, норм, спс).
    private static readonly Lazy<HashSet<string>> RussianColloquial = new(() =>
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KeySwitch.ru-colloquial.txt");
        if (stream is null) return words;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            if (line.Trim() is { Length: > 0 } word && !word.StartsWith('#')) words.Add(Plain(word.ToLowerInvariant()));
        return words;
    });
    public static bool IsColloquial(string word) => RussianColloquial.Value.Contains(Plain(word.ToLowerInvariant()));
    // Adjacent word pair counts (data/ru-pairs.bin), only valid together with the word-form list they were built for.
    private static readonly Lazy<PairModel?> Pairs = new(() => RussianFormList ? PairModel.Load(Ru.Value.Words.Count) : null);
    /// <summary>True when the word-pair context table is bundled and matches the bundled form list.</summary>
    public static bool HasPairContext => Pairs.Value is not null;
    private readonly DecisionEngine layout;
    public TypoCorrector(DecisionEngine? layout = null) => this.layout = layout ?? new DecisionEngine();

    public TypoDecision Evaluate(string word, string? previous = null, string? previous2 = null,
        IEnumerable<string>? exceptions = null) => Evaluate(word, previous, previous2, exceptions, TypoPolicy.Auto);

    /// <summary>Spelling fix the user asked for (Pause / double Shift) with <see cref="TypoPolicy.Manual"/>: works
    /// while Russian auto-correction is off and has a lower bar. A capitalized word is fixed in lower case and
    /// re-capitalized.</summary>
    public TypoDecision Suggest(string word, string? previous = null, string? previous2 = null,
        IEnumerable<string>? exceptions = null, TypoPolicy? policy = null)
    {
        bool capitalized = word.Length > 1 && char.IsUpper(word[0]) && !word.Skip(1).Any(char.IsUpper);
        string lower = capitalized ? char.ToLowerInvariant(word[0]) + word[1..] : word;
        var decision = Evaluate(lower, previous, previous2, exceptions, policy ?? TypoPolicy.Manual);
        if (!decision.ShouldCorrect || !capitalized) return decision with { Original = word };
        return decision with { Original = word, Corrected = char.ToUpperInvariant(decision.Corrected[0]) + decision.Corrected[1..] };
    }

    /// <summary>Minimum candidate lead for <see cref="Suggest"/>: the lowest dev-set threshold (TypoEval --pairs,
    /// ManualSweep) at which at least 95% of suggestions for real typos are the intended word
    /// (dev, whole form list: 95.8% right, 39.5% of typos fixed).</summary>
    public const double ManualMargin = 2.5;

    /// <summary>Decides with explicit thresholds; the shipped modes are <see cref="TypoPolicy.Auto"/> and
    /// <see cref="TypoPolicy.Manual"/>, TypoEval sweeps other values on the dev set.</summary>
    public TypoDecision Evaluate(string word, string? previous, string? previous2, IEnumerable<string>? exceptions, TypoPolicy policy)
    {
        TypoDecision Keep(string reason) => new(false, word, word, 0, reason);
        if (word.Length < 4 || word.Length > 32 || !word.All(char.IsLetter)) return Keep("protected-shape");
        if (word.Any(char.IsDigit) || word.All(char.IsUpper) || char.IsUpper(word[0]) || word.Skip(1).Any(char.IsUpper))
            return Keep("protected-case");
        if (exceptions?.Any(x => string.Equals(x, word, StringComparison.OrdinalIgnoreCase)) == true) return Keep("exception");
        bool russian = word.All(c => c is >= 'а' and <= 'я' or 'ё');
        bool english = word.All(c => c is >= 'a' and <= 'z');
        if (!russian && !english) return Keep("protected-script");
        if (russian && !RussianEnabled && !policy.IgnoreRussianSwitch) return Keep("ru-typo-disabled");
        // Four-letter Russian words have too many real neighbours (таку -> так, блан -> план) to change safely.
        if (russian && word.Length < policy.RussianMinLength) return Keep("protected-shape");
        if (layout.IsKnownWord(word, russian))
            return russian && RealWord(word, previous, policy) is { } realWord ? realWord : Keep("known-original");
        if (russian && RussianSeen.Value.Contains(Plain(word.ToLowerInvariant()))) return Keep("known-corpus");
        if (russian && IsColloquial(word)) return Keep("colloquial");
        var data = russian ? Ru.Value : En.Value;
        string lower = word.ToLowerInvariant();
        int maxDistance = !russian && lower.Length >= 8 ? 2 : 1;
        var candidates = new HashSet<Entry>();
        if (russian)
        {
            foreach (var edit in SingleEdits(lower, RussianLetters))
                if (data.Find(edit) is { } entry) candidates.Add(entry);
            // Two edits only as a fallback for long words, and only when the policy asks for it: a bounded scan
            // of the ranked forms within two letters of the typed length (early-exit edit distance).
            if (candidates.Count == 0 && lower.Length >= policy.RussianDistance2MinLength)
            {
                maxDistance = 2;
                string plainTyped = Plain(lower);
                for (int length = lower.Length - 2; length <= lower.Length + 2; length++)
                    foreach (var entry in data.OfLength(length))
                        if (EditDistance(plainTyped, Plain(entry.Word), 2) == 2) candidates.Add(entry);
            }
        }
        else
            foreach (var deleted in DeletesOf(lower, maxDistance))
                if (data.Deletes.TryGetValue(deleted, out var entries))
                    foreach (var entry in entries) candidates.Add(entry);
        // The exact word is in neither ranked lexicon nor the Bloom filter here.
        var scored = new List<(Entry Entry, double Score, double Cost)>();
        foreach (var entry in candidates)
        {
            if (Math.Abs(entry.Word.Length - lower.Length) > maxDistance) continue;
            string plain = Plain(entry.Word);
            int distance = EditDistance(Plain(lower), plain, maxDistance);
            if (distance == 0 || distance > maxDistance) continue;
            // A second English edit requires a common destination and a plausible word shape.
            if (!russian && distance == 2 && (entry.Rank > 10000 || lower.Length < 8)) continue;
            double cost = distance == 2 ? 3.2 : EditCost(Plain(lower), plain, russian);
            scored.Add((entry, Score(data, entry.Word, entry.Rank, cost, previous, previous2, russian, policy), cost));
        }
        if (scored.Count == 0) return Keep("no-candidate");
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = scored[0];
        double margin = scored.Count == 1 ? 4 : best.Score - scored[1].Score;
        // A strict margin protects unknown names and foreign words absent from the Bloom lexicon.
        // The Russian list holds word forms, so the same frequency spans far more ranks than in a lemma list.
        double required = policy.MinMargin;
        if (margin < required || best.Entry.Rank > (russian ? policy.RussianRankCap : policy.EnglishRankCap)) return Keep("ambiguous-candidate");
        // The ranked list holds only frequent words. A real form from the full lexicon (OpenCorpora for RU) one edit
        // away is scored as if it were just below the ranked list (its true frequency can only be lower); if it
        // comes within the margin of the best candidate, the intended word is unclear.
        if (russian && BestUnrankedRival(lower, data, best.Entry.Word, previous, previous2, policy) is double rival)
        {
            if (best.Score - rival < required) return Keep("ambiguous-form");
            margin = Math.Min(margin, best.Score - rival);
        }
        if (russian && margin < policy.RussianMinMargin) return Keep("ambiguous-candidate");
        // The corpus has seen the previous word often enough to expect the candidate after it, yet never did.
        if (russian && Pairs.Value is { } pairs && pairs.Lift(previous, best.Entry.Rank) < policy.ContextVeto) return Keep("context-mismatch");
        return new(true, word, best.Entry.Word, margin, policy.Reason);
    }

    /// <summary>A real word typed instead of another one (стаей for статей, учится for учиться, напишите for
    /// напишете), decided by the previous word: a ranked form one edit away must follow that word in the corpus at
    /// least <see cref="TypoPolicy.RealWordMinPairs"/> times and e^<see cref="TypoPolicy.RealWordMargin"/> times as
    /// often as the typed word and as every other such form. Only the confusion kinds the policy names are used.</summary>
    private TypoDecision? RealWord(string word, string? previous, TypoPolicy policy)
    {
        if (double.IsPositiveInfinity(policy.RealWordMargin) || policy.RealWordKinds == RealWordKinds.None ||
            word.Length < policy.RussianMinLength || Pairs.Value is not { } pairs || pairs.PreviousCount(previous) is null) return null;
        var data = Ru.Value;
        string lower = word.ToLowerInvariant();
        int typedRank = data.Find(lower)?.Rank ?? 0;
        double typedPairs = pairs.PairCount(previous, typedRank), typedCount = pairs.FormCount(typedRank);
        Entry? best = null;
        double bestPairs = 0, secondPairs = 0;
        RealWordKinds bestKind = RealWordKinds.None;
        foreach (var edit in SingleEdits(Plain(lower), RussianLetters))
        {
            if (data.Find(edit) is not { } entry || entry.Rank > policy.RussianRankCap || entry.Rank == typedRank) continue;
            var kind = Confusion(lower, entry.Word);
            if ((kind & policy.RealWordKinds) == 0) continue;
            // Any other one-letter confusion only towards a much more frequent word (стаей -> статей).
            if (kind == RealWordKinds.OtherEdit && pairs.FormCount(entry.Rank) < 10 * typedCount) continue;
            double count = pairs.PairCount(previous, entry.Rank);
            if (count > bestPairs) { secondPairs = bestPairs; bestPairs = count; best = entry; bestKind = kind; }
            else secondPairs = Math.Max(secondPairs, count);
        }
        if (best is null || bestPairs < policy.RealWordMinPairs) return null;
        double margin = Math.Log((bestPairs + 0.5) / (Math.Max(typedPairs, secondPairs) + 0.5));
        if (margin < policy.RealWordMargin) return null;
        return new(true, word, best.Word, margin, policy.Reason + "-context-" + bestKind switch
        {
            RealWordKinds.Tsya => "tsya", RealWordKinds.SecondPlural => "2pl", _ => "edit",
        });
    }

    /// <summary>How two Russian words one edit apart are confused: тся/ться, the 2nd person plural ете/ите
    /// (напишете/напишите), or any other single edit; <see cref="RealWordKinds.None"/> otherwise.</summary>
    public static RealWordKinds Confusion(string typed, string intended)
    {
        string a = Plain(typed.ToLowerInvariant()), b = Plain(intended.ToLowerInvariant());
        if (a == b || EditDistance(a, b, 1) != 1) return RealWordKinds.None;
        static bool Tsya(string x, string y) => x.EndsWith("тся", StringComparison.Ordinal) && y.EndsWith("ться", StringComparison.Ordinal) && x[..^3] == y[..^4];
        if (Tsya(a, b) || Tsya(b, a)) return RealWordKinds.Tsya;
        if (a.Length == b.Length && a.Length > 4 && a[..^3] == b[..^3] && a.EndsWith("те", StringComparison.Ordinal) && b.EndsWith("те", StringComparison.Ordinal) &&
            (a[^3], b[^3]) is ('е', 'и') or ('и', 'е'))
            return RealWordKinds.SecondPlural;
        return RealWordKinds.OtherEdit;
    }

    private double Score(LanguageData data, string candidate, int rank, double cost, string? previous, string? previous2, bool russian, TypoPolicy policy)
    {
        double frequency = -Math.Log(rank + 15);
        double language = data.LanguageScore(candidate);
        double modelLanguage = Math.Log(Math.Max(1e-6, layout.LanguageProbability(candidate, previous, previous2)));
        double context = ContextScore(previous, previous2, russian);
        // Word-pair context: how typical the candidate is after the previous word (unranked forms: neutral).
        if (russian && policy.ContextWeight != 0 && Pairs.Value is { } pairs) context += policy.ContextWeight * pairs.Lift(previous, rank);
        return frequency - 1.5 * cost + 0.65 * language + 0.3 * modelLanguage + context;
    }

    internal const int RussianRankCap = 50000;
    // Minimum lead of the best Russian candidate over every rival, calibrated per candidate list on the dev set
    // (TypoEval --pairs): 4.0 with the word-form list (test: 88.8% precision, 22.8% recall), 2.2 with the lemma list.
    internal static double RussianMargin => RussianFormList ? 4.0 : 2.2;
    private const string RussianLetters = "абвгдежзийклмнопрстуфхцчшщъыьэюя";

    private double? BestUnrankedRival(string lower, LanguageData data, string chosen, string? previous, string? previous2, TypoPolicy policy)
    {
        double? best = null;
        foreach (string form in SingleEdits(lower, RussianLetters))
        {
            if (form == Plain(chosen) || form.Length < 2 || data.Ranked(form) || !layout.IsKnownWord(form, true)) continue;
            double score = Score(data, form, data.Words.Count + 1, EditCost(lower, form, true), previous, previous2, true, policy);
            if (best is null || score > best) best = score;
        }
        return best;
    }

    private static IEnumerable<string> SingleEdits(string word, string alphabet)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < word.Length; i++)
        {
            string deleted = word.Remove(i, 1);
            if (seen.Add(deleted)) yield return deleted;
            if (i + 1 < word.Length)
            {
                string swapped = word[..i] + word[i + 1] + word[i] + word[(i + 2)..];
                if (seen.Add(swapped)) yield return swapped;
            }
            foreach (char c in alphabet)
            {
                if (c == word[i]) continue;
                string replaced = word[..i] + c + word[(i + 1)..];
                if (seen.Add(replaced)) yield return replaced;
            }
        }
        for (int i = 0; i <= word.Length; i++)
            foreach (char c in alphabet)
            {
                string inserted = word.Insert(i, c.ToString());
                if (seen.Add(inserted)) yield return inserted;
            }
    }

    private static double EditCost(string typed, string candidate, bool russian)
    {
        int first = 0;
        while (first < typed.Length && first < candidate.Length && typed[first] == candidate[first]) first++;
        // Russian writers rarely mistype the first letter, but often drop or add ь/ъ (будеш, собираешся, хочеться).
        double initial = russian && first == 0 ? 0.5 : 0;
        if (IsTransposition(typed, candidate)) return 0.55 + initial;
        if (Math.Abs(typed.Length - candidate.Length) == 1)
        {
            string longer = typed.Length > candidate.Length ? typed : candidate;
            if (russian && longer[first] is 'ь' or 'ъ')
            {
                // A missing ь after ш in the 2nd person (будеш, собираешся) is a spelling habit, not a slip.
                bool secondPerson = candidate.Length > typed.Length && first > 0 && candidate[first - 1] == 'ш' &&
                    (first == candidate.Length - 1 || candidate.AsSpan(first + 1).SequenceEqual("ся"));
                return secondPerson ? 0 : 0.3;
            }
            return 0.75 + initial;
        }
        if (IsAdjacentSubstitution(typed, candidate, russian)) return 0.65 + initial;
        return 1.0 + initial;
    }

    private static double ContextScore(string? previous, string? previous2, bool russian)
    {
        // Context confirms the script; all candidates for a word use the same language.
        // It is deliberately too small to override edit and frequency evidence.
        bool Match(string? value) => value is { Length: > 0 } &&
            (russian ? value.Any(c => c is >= 'а' and <= 'я' or 'ё') : value.Any(c => c is >= 'a' and <= 'z'));
        return (Match(previous) ? 0.15 : 0) + (Match(previous2) ? 0.05 : 0);
    }

    private static IEnumerable<string> DeletesOf(string word, int depth)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { word };
        var frontier = new List<string> { word };
        for (int pass = 0; pass < depth; pass++)
        {
            var next = new List<string>();
            foreach (var source in frontier)
                for (int i = 0; i < source.Length; i++)
                {
                    string deleted = source.Remove(i, 1);
                    if (seen.Add(deleted)) next.Add(deleted);
                }
            frontier = next;
        }
        return seen;
    }

    private static int EditDistance(string a, string b, int maximum)
    {
        if (IsTransposition(a, b)) return 1;
        if (Math.Abs(a.Length - b.Length) > maximum) return maximum + 1;
        int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
        int[] current = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowMinimum = current[0];
            for (int j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }
            if (rowMinimum > maximum) return maximum + 1;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    private static bool IsTransposition(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int first = 0;
        while (first < a.Length && a[first] == b[first]) first++;
        return first + 1 < a.Length && a[first] == b[first + 1] && a[first + 1] == b[first] && a[(first + 2)..] == b[(first + 2)..];
    }
    private static bool IsAdjacentSubstitution(string a, string b, bool russian)
    {
        if (a.Length != b.Length) return false;
        int mismatch = -1;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) { if (mismatch >= 0) return false; mismatch = i; }
        if (mismatch < 0) return false;
        string[] rows = russian ? ["йцукенгшщзхъ", "фывапролджэ", "ячсмитьбю"] : ["qwertyuiop", "asdfghjkl", "zxcvbnm"];
        for (int r = 0; r < rows.Length; r++)
        {
            int x = rows[r].IndexOf(a[mismatch]), y = rows[r].IndexOf(b[mismatch]);
            if (x >= 0 && y >= 0 && Math.Abs(x - y) == 1) return true;
            if (x >= 0)
                for (int other = Math.Max(0, r - 1); other <= Math.Min(rows.Length - 1, r + 1); other++)
                    if (other != r && (y = rows[other].IndexOf(b[mismatch])) >= 0 && Math.Abs(x - y) <= 1) return true;
        }
        return false;
    }
}

/// <summary>Thresholds of one correction mode. <see cref="Auto"/> and <see cref="Manual"/> are what KeySwitch uses;
/// TypoEval --pairs evaluates other values on the dev set with exactly the same code path.</summary>
public sealed record TypoPolicy(double MinMargin, double RussianMinMargin, int RussianRankCap, int EnglishRankCap,
    int RussianMinLength, bool IgnoreRussianSwitch, int RussianDistance2MinLength, string Reason)
{
    /// <summary>Automatic correction at a word boundary.</summary>
    /// <remarks>Word-pair context (dev, TypoEval --pairs): candidate weight 0.5 (lead 4.0: 89.74% precision / 20.44% recall vs
    /// 89.26% / 19.43% without) and тся/ться real-word fixes after a typical previous word (pairs >= 20, lead >= 3:
    /// 4 right, 0 changed correct words). Other real-word confusions (стаей/статей) changed far more correct words
    /// than typos on dev (at best 32% right), so they stay off; ете/ите did not occur on dev and stays off too.</remarks>
    public static TypoPolicy Auto => new(2.2, TypoCorrector.RussianMargin, TypoCorrector.RussianRankCap, 12000, 5, false, int.MaxValue, "typo-autocorrect")
    {
        ContextWeight = 0.5, RealWordMargin = 3, RealWordMinPairs = 20, RealWordKinds = RealWordKinds.Tsya,
    };
    /// <summary>Pause / double Shift on a word: works with Russian auto-correction off, lower bar, whole form list
    /// (dev: rank cap 200k beats 50k on both accuracy and coverage). Two edits stay off in both modes: on the dev
    /// set they lowered precision at every threshold, also for 8+ letter words.</summary>
    public static TypoPolicy Manual => new(TypoCorrector.ManualMargin, TypoCorrector.ManualMargin, 200000, 12000, 4, true, int.MaxValue, "typo-manual");

    /// <summary>Weight of the word-pair lift (data/ru-pairs.bin) in the Russian candidate score; 0 = off.</summary>
    public double ContextWeight { get; init; }
    /// <summary>Keep the word when the best candidate's word-pair lift after the previous word is below this.</summary>
    public double ContextVeto { get; init; } = double.NegativeInfinity;
    /// <summary>Real-word errors (a known word typed instead of another): minimum log ratio of pair counts;
    /// infinity = off.</summary>
    public double RealWordMargin { get; init; } = double.PositiveInfinity;
    /// <summary>Real-word errors: minimum corpus count of (previous word, intended word).</summary>
    public double RealWordMinPairs { get; init; } = 20;
    /// <summary>Real-word errors: which confusions are corrected.</summary>
    public RealWordKinds RealWordKinds { get; init; } = RealWordKinds.Tsya | RealWordKinds.SecondPlural;
}

[Flags]
public enum RealWordKinds
{
    None = 0,
    /// <summary>учится / учиться</summary>
    Tsya = 1,
    /// <summary>напишете / напишите</summary>
    SecondPlural = 2,
    /// <summary>Any other single edit towards a much more frequent word (стаей / статей).</summary>
    OtherEdit = 4,
}
