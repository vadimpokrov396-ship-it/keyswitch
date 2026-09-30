using System.Reflection;
namespace KeySwitch.Core;

public sealed record DecisionResult(bool ShouldConvert, string Original, string Converted, double Confidence, string Reason);

/// <summary>Offline, document-trained character classifier. No text leaves the process.</summary>
public sealed class DecisionEngine
{
    private const int Dim = 1 << 16;
    private static readonly Lazy<(float[] Weights, float Bias, float Threshold)> Model = new(Load);
    private static readonly Lazy<(BloomLexicon En, BloomLexicon Ru)> Lexicons = new(() =>
        (new("KeySwitch.en-forms.bloom"), new("KeySwitch.ru-forms.bloom")));
    private static readonly HashSet<string> Frequent = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "you", "that", "this", "with", "from", "have", "your", "are", "was", "but", "not", "can", "our", "all", "one", "has", "will",
        "это", "что", "как", "для", "его", "она", "они", "при", "или", "есть", "так", "все", "уже", "где", "если", "нет", "нам", "вам"
    };
    private static readonly HashSet<string> DomainSuffixes = new(StringComparer.OrdinalIgnoreCase)
        { "com", "org", "net", "ru", "io", "wiki", "site", "co", "uk", "рф" };
    public DecisionEngine() { _ = Model.Value; _ = Lexicons.Value; }
    public DecisionResult Evaluate(string token, IEnumerable<string>? exceptions = null) => Evaluate(token, null, null, exceptions);
    public bool IsKnownWord(string word, bool russian) => (russian ? Lexicons.Value.Ru : Lexicons.Value.En).Contains(word);
    /// <summary>Probability assigned by the embedded trained layout model that a word belongs to its current script.</summary>
    public double LanguageProbability(string word, string? previous = null, string? previous2 = null)
    {
        if (word.Length == 0) return 0;
        double logit = RawModelScore(word, previous, previous2);
        double difference = Math.Clamp(Model.Value.Threshold - logit, -30, 30);
        return 1.0 / (1.0 + Math.Exp(-difference));
    }
    public DecisionResult Evaluate(string token, string? previous, string? previous2, IEnumerable<string>? exceptions = null)
    {
        string converted = LayoutMap.Convert(token);
        DecisionResult Keep(string reason, double score = 0) => new(false, token, converted, score, reason);
        if (token.Length < 1 || token.Length > 64) return Keep("length");
        if (exceptions?.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase) || string.Equals(x, converted, StringComparison.OrdinalIgnoreCase)) == true) return Keep("exception");
        if (token.Any(c => char.IsDigit(c) || char.IsWhiteSpace(c) || "@/\\_-+=#".Contains(c))) return Keep("structured-token");
        if (token.Contains("://", StringComparison.Ordinal) || token.Contains("..", StringComparison.Ordinal)) return Keep("structured-token");
        int dot = token.LastIndexOf('.');
        if (dot > 0 && dot < token.Length - 1)
        {
            string suffix = token[(dot + 1)..].ToLowerInvariant();
            if (DomainSuffixes.Contains(suffix) || DomainSuffixes.Contains(LayoutMap.Convert(suffix))) return Keep("domain-or-dot");
        }
        bool latin = token.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        bool cyrillic = token.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        if (latin == cyrillic) return Keep("mixed-or-unsupported-script");
        if (Script(converted) == "other") return Keep("mixed-target");
        if (token.Skip(1).Any(char.IsUpper) && !token.Where(char.IsLetter).All(char.IsUpper)) return Keep("identifier");
        if (token.Length > 1 && token.Length <= 4 && token.Where(char.IsLetter).All(char.IsUpper)) return Keep("abbreviation");
        if (!token.All(c => char.IsLetter(c) || "`[];',.~{}:\"<>".Contains(c))) return Keep("symbols");
        if (token.Contains('.') && token.Count(c => c == '.') > 1) return Keep("domain-or-dot");
        string lower = token.ToLowerInvariant(), other = converted.ToLowerInvariant();
        var (enLexicon, ruLexicon) = Lexicons.Value;
        var sourceLexicon = latin ? enLexicon : ruLexicon;
        var targetLexicon = latin ? ruLexicon : enLexicon;
        bool targetContext = Script(previous ?? "") == Script(other) || Script(previous2 ?? "") == Script(other);
        bool targetKnown = targetLexicon.Contains(other);
        if (token.Length <= 3 && targetContext && targetKnown && !Frequent.Contains(token))
            return new(true, token, converted, 1, "context-short-word");
        if (token.Length == 1) return Keep("length");
        if (Frequent.Contains(token) || sourceLexicon.Contains(lower)) return Keep("known-original");
        var (_,_,threshold) = Model.Value;
        double logit = RawModelScore(token, previous, previous2);
        // Scores are logits; threshold is calibrated against correct, held-out sentences.
        return logit >= threshold ? new(true, token, converted, logit, "trained-model") : Keep("model-below-threshold", logit);
    }
    private static double RawModelScore(string token, string? previous, string? previous2)
    {
        string lower = token.ToLowerInvariant(), other = LayoutMap.Convert(token).ToLowerInvariant();
        var (weights,bias,_) = Model.Value;
        double logit = bias;
        void Add(string feature) { logit += weights[Hash(feature)]; }
        foreach (var (prefix, value) in new[] { ("o", lower), ("c", other) })
        {
            string padded = "^" + value + "$";
            for (int n = 1; n <= 5; n++)
                for (int i = 0; i + n <= padded.Length; i++) Add(prefix + n + ":" + padded.Substring(i, n));
        }
        Add("script:" + Script(lower));
        Add("len:" + Math.Min(lower.Length, 16));
        var context = new[] { previous2, previous };
        int index = 0;
        foreach (var prior in context.Where(p => !string.IsNullOrEmpty(p)))
        {
            Add("ctx" + index + ":" + Script(prior!));
            Add("ctx" + index + "last:" + prior!.ToLowerInvariant()[^Math.Min(2, prior.Length)..]);
            index++;
        }
        return logit;
    }
    private static string Script(string value)
    {
        bool en = value.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        bool ru = value.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        return en && !ru ? "en" : ru && !en ? "ru" : "other";
    }
    private static int Hash(string value)
    {
        uint hash = 2166136261;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value)) hash = (hash ^ b) * 16777619;
        return (int)(hash & (Dim - 1));
    }
    private static (float[],float,float) Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KeySwitch.layout-model.bin") ?? throw new InvalidOperationException("Missing trained model");
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "KSM1" || reader.ReadInt32() != Dim) throw new InvalidDataException("Invalid model");
        float threshold = reader.ReadSingle();
        var weights = new float[Dim];
        for (int i = 0; i < Dim; i++) weights[i] = reader.ReadSingle();
        return (weights, reader.ReadSingle(), threshold);
    }
}
