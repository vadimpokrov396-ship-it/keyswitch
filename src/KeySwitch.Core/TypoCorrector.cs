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
        public readonly Dictionary<string, Entry> Words = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<Entry>> Deletes = new(StringComparer.Ordinal);
        // Ranked words with ё spelled е, as users usually type them.
        private readonly HashSet<string> plainWords = new(StringComparer.Ordinal);
        public bool Ranked(string word) => plainWords.Contains(word.Replace('ё', 'е'));
        public readonly Dictionary<string, double> Trigrams = new(StringComparer.Ordinal);
        public double TotalTrigrams;
        public readonly int Alphabet;
        public LanguageData(string resource, int alphabet)
        {
            Alphabet = alphabet;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing {resource}");
            using var reader = new StreamReader(stream);
            int rank = 0;
            while (reader.ReadLine() is { } line)
            {
                var word = line.Trim().ToLowerInvariant();
                if (word.Length < 2 || word.Length > 32 || !word.All(char.IsLetter) || Words.ContainsKey(word)) continue;
                var entry = new Entry(word, ++rank);
                Words.Add(word, entry);
                plainWords.Add(word.Replace('ё', 'е'));
                // Full one-edit vocabulary; the two-edit index is limited to frequent words.
                foreach (var deleted in DeletesOf(word, rank <= 15000 ? 2 : 1))
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
    private static readonly Lazy<LanguageData> Ru = new(() => new("KeySwitch.ru.txt", 33));
    private readonly DecisionEngine layout;
    public TypoCorrector(DecisionEngine? layout = null) => this.layout = layout ?? new DecisionEngine();

    public TypoDecision Evaluate(string word, string? previous = null, string? previous2 = null,
        IEnumerable<string>? exceptions = null)
    {
        TypoDecision Keep(string reason) => new(false, word, word, 0, reason);
        if (word.Length < 4 || word.Length > 32 || !word.All(char.IsLetter)) return Keep("protected-shape");
        if (word.Any(char.IsDigit) || word.All(char.IsUpper) || char.IsUpper(word[0]) || word.Skip(1).Any(char.IsUpper))
            return Keep("protected-case");
        if (exceptions?.Any(x => string.Equals(x, word, StringComparison.OrdinalIgnoreCase)) == true) return Keep("exception");
        bool russian = word.All(c => c is >= 'а' and <= 'я' or 'ё');
        bool english = word.All(c => c is >= 'a' and <= 'z');
        if (!russian && !english) return Keep("protected-script");
        if (russian && !RussianEnabled) return Keep("ru-typo-disabled");
        if (layout.IsKnownWord(word, russian)) return Keep("known-original");
        var data = russian ? Ru.Value : En.Value;
        string lower = word.ToLowerInvariant();
        int maxDistance = lower.Length >= 8 ? 2 : 1;
        var candidates = new HashSet<Entry>();
        foreach (var deleted in DeletesOf(lower, maxDistance))
            if (data.Deletes.TryGetValue(deleted, out var entries))
                foreach (var entry in entries) candidates.Add(entry);
        // The exact word is in neither ranked lexicon nor the Bloom filter here.
        var scored = new List<(Entry Entry, double Score, double Cost)>();
        foreach (var entry in candidates)
        {
            if (Math.Abs(entry.Word.Length - lower.Length) > maxDistance) continue;
            int distance = EditDistance(lower, entry.Word, maxDistance);
            if (distance == 0 || distance > maxDistance) continue;
            // A second edit requires a common destination and a plausible word shape.
            if (distance == 2 && (entry.Rank > 10000 || lower.Length < 8)) continue;
            double cost = distance == 2 ? 3.2 : EditCost(lower, entry.Word, russian);
            double frequency = -Math.Log(entry.Rank + 15);
            double language = data.LanguageScore(entry.Word);
            double modelLanguage = Math.Log(Math.Max(1e-6, layout.LanguageProbability(entry.Word, previous, previous2)));
            double context = ContextScore(previous, previous2, russian);
            scored.Add((entry, frequency - 1.5 * cost + 0.65 * language + 0.3 * modelLanguage + context, cost));
        }
        if (scored.Count == 0) return Keep("no-candidate");
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = scored[0];
        double margin = scored.Count == 1 ? 4 : best.Score - scored[1].Score;
        // A strict margin protects unknown names and foreign words absent from the Bloom lexicon.
        if (margin < 2.2 || best.Entry.Rank > 12000) return Keep("ambiguous-candidate");
        // The ranked list holds only frequent words. A real form from the full lexicon (OpenCorpora for RU) that is
        // one edit away and at least as plausible an edit is an unranked rival: the intended word is unclear.
        if (russian && HasUnrankedRival(lower, data, best.Entry.Word, best.Cost)) return Keep("ambiguous-form");
        return new(true, word, best.Entry.Word, margin, "typo-autocorrect");
    }

    private const string RussianLetters = "абвгдежзийклмнопрстуфхцчшщъыьэюя";

    private bool HasUnrankedRival(string lower, LanguageData data, string chosen, double chosenCost)
    {
        foreach (string form in SingleEdits(lower, RussianLetters))
        {
            if (form == chosen || form.Length < 2 || data.Ranked(form) || !layout.IsKnownWord(form, true)) continue;
            if (EditCost(lower, form, true) <= chosenCost + RivalCostSlack) return true;
        }
        return false;
    }

    private const double RivalCostSlack = 0.25;

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
        if (IsTransposition(typed, candidate)) return 0.55;
        if (Math.Abs(typed.Length - candidate.Length) == 1) return 0.75;
        if (IsAdjacentSubstitution(typed, candidate, russian)) return 0.65;
        return 1.0;
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
