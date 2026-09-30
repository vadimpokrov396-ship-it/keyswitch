using System.Globalization;
using System.Reflection;
namespace KeySwitch.Core;

/// <summary>Offline language decision model. Known original words are protected.</summary>
public sealed class LegacyDecisionEngine
{
    private sealed class Model
    {
        public readonly Dictionary<string, long> Words = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> triples = new(StringComparer.Ordinal);
        private double total;
        public Model(params string[] resources)
        {
            foreach (var resource in resources)
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"Missing lexicon {resource}");
                using var reader = new StreamReader(stream);
                int rank = 0;
                while (reader.ReadLine() is { } line)
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;
                    rank++;
                    long frequency = Math.Max(1, 1_000_000 / rank);
                    if (parts.Length > 1 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) frequency = count;
                    var word = parts[0].ToLowerInvariant();
                    Words[word] = Math.Max(frequency, 1);
                    var weight = Math.Log(1 + Math.Max(frequency, 1));
                    var padded = "^^" + word + "$";
                    for (int i = 0; i < padded.Length - 2; i++)
                    {
                        string triple = padded.Substring(i, 3);
                        triples[triple] = triples.GetValueOrDefault(triple) + weight;
                        total += weight;
                    }
                }
            }
        }
        public double LanguageScore(string word)
        {
            var padded = "^^" + word.ToLowerInvariant() + "$";
            double score = 0;
            for (int i = 0; i < padded.Length - 2; i++) score += Math.Log((triples.GetValueOrDefault(padded.Substring(i, 3)) + .1) / (total + .1 * triples.Count));
            return score / (padded.Length - 2);
        }
        public bool Contains(string word) => Words.ContainsKey(word);
    }
    private static readonly Lazy<(Model En, Model Ru)> Models = new(() => (new("KeySwitch.en.txt"), new("KeySwitch.ru.txt", "KeySwitch.ru-common.txt")));
    public LegacyDecisionEngine() { _ = Models.Value; }

    public DecisionResult Evaluate(string token, IEnumerable<string>? exceptions = null, bool targetContext = false)
    {
        var converted = LayoutMap.Convert(token);
        DecisionResult Keep(string reason) => new(false, token, converted, 0, reason);
        if (token.Length < 1 || token.Length > 64) return Keep("length");
        if (exceptions?.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase) || string.Equals(x, converted, StringComparison.OrdinalIgnoreCase)) == true) return Keep("exception");
        if (token.Any(char.IsDigit) || token.Any(char.IsWhiteSpace) || token.IndexOfAny(['@', '/', '\\', '_', '-', ':', '+', '=', '#']) >= 0) return Keep("structured-token");
        bool latin = token.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        bool cyrillic = token.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        if (latin == cyrillic) return Keep("mixed-or-unsupported-script");
        if (token.Skip(1).Any(char.IsUpper) && !token.Where(char.IsLetter).All(char.IsUpper)) return Keep("identifier");
        if (token.Length <= 4 && token.Where(char.IsLetter).All(char.IsUpper)) return Keep("abbreviation");
        if (!token.All(c => char.IsLetter(c) || "`[];',.".Contains(c))) return Keep("symbols");
        if (token.Count(c => c == '.') > 1 || (token.Contains('.') && token.IndexOf('.') < token.Length - 1)) return Keep("domain-or-dot");
        if (!converted.All(c => char.IsLetter(c) || c == '\'')) return Keep("unsupported-target");
        var (en, ru) = Models.Value;
        string lower = token.ToLowerInvariant();
        string target = converted.ToLowerInvariant();
        var from = latin ? en : ru;
        var to = latin ? ru : en;
        if (from.Contains(lower) && !(targetContext && token.Length <= 3 && to.Contains(target))) return Keep("known-original");
        if (token.Length == 1)
            return targetContext && to.Contains(target)
                ? new(true, token, converted, 1, "context-single-letter") : Keep("length");
        if (targetContext && token.Length <= 3 && to.Contains(target))
            return new(true, token, converted, 1, "context-short-word");
        // Ambiguous short words need context. This includes Latin-looking words such as "ne".
        if (token.Length <= 3 && (en.Contains(lower) || ru.Contains(lower)) && !targetContext) return Keep("ambiguous-short");
        double sourceScore = from.LanguageScore(lower);
        double targetScore = to.LanguageScore(target);
        double margin = targetScore - sourceScore;
        bool targetKnown = to.Contains(target);
        if (targetKnown)
        {
            if (margin >= (targetContext ? -1.0 : 0.25))
                return new(true, token, converted, margin, "known-target-and-language-margin");
            return Keep("ambiguous-score");
        }
        // Inflected forms often miss the word list; phonotactics must be strong on both sides.
        if (token.Length >= 4 && sourceScore <= -10.0 && targetScore >= -9.1 && margin >= 2.6)
            return new(true, token, converted, margin, "trigram-unknown-target");
        return Keep("insufficient-language-evidence");
    }

    public bool IsKnownWord(string token, bool russian)
    {
        var (en, ru) = Models.Value;
        return (russian ? ru : en).Contains(token);
    }
}
