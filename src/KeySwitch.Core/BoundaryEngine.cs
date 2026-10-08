namespace KeySwitch.Core;

public sealed record ManualFix(string Replacement, bool LayoutChange, string Reason);

public sealed record BoundaryResult(string Original, string Replacement, bool Changed, int PreviousCharacters, string Reason, double Confidence);

/// <summary>Shared live-input and corpus replay boundary policy. State is memory-only.</summary>
public sealed class BoundaryEngine(DecisionEngine engine, bool legacy = false,
    Func<string, IEnumerable<string>?, DecisionResult>? baselineEvaluator = null)
{
    private readonly TypoCorrector typo = new(engine);
    public bool LayoutEnabled { get; set; } = true;
    public bool TypoEnabled { get; set; } = true;
    public IEnumerable<string>? TypoExceptions { get; set; }
    private readonly List<string> context = new();
    private string? previousUnconvertedShort;
    private readonly LegacyDecisionEngine oldEngine = new();
    public static bool IsTokenCharacter(char c) => char.IsLetterOrDigit(c) || "`,.;'[]~{}:\"<>@/_-\\#?&=%+".Contains(c);
    public void Reset() { context.Clear(); previousUnconvertedShort = null; }

    /// <summary>What Pause / double Shift does to a word: text typed in the wrong layout that forms a known word
    /// is converted; otherwise a confident spelling fix (typo-manual) is applied; otherwise the layout conversion
    /// as before. Spelling fixes keep the script, so no layout switch follows.</summary>
    public ManualFix Manual(string token, IEnumerable<string>? exceptions = null)
    {
        string converted = LayoutMap.Convert(token);
        bool convertedRussian = converted.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        if (converted.All(char.IsLetter) && engine.IsKnownWord(converted, convertedRussian)) return new(converted, true, "manual-layout-known");
        var suggestion = typo.Suggest(token, null, null, exceptions);
        if (suggestion.ShouldCorrect) return new(suggestion.Corrected, false, suggestion.Reason);
        return new(converted, true, "manual-layout");
    }
    /// <summary>What successive Pause presses offer, like T9 cycling: <see cref="Manual"/> first, then up to two
    /// further spellings of a misspelled word (same script, no layout switch). After the last one Pause restores the
    /// typed word.</summary>
    public IReadOnlyList<ManualFix> ManualOptions(string token, IEnumerable<string>? exceptions = null)
    {
        var first = Manual(token, exceptions);
        var options = new List<ManualFix> { first };
        if (token.All(char.IsLetter))
            foreach (var spelled in typo.Alternatives(token, first.Replacement, exceptions: exceptions))
                options.Add(new(spelled, false, "manual-alternative"));
        return options;
    }

    public BoundaryResult Complete(string token, string separator, IEnumerable<string>? exceptions = null)
    {
        if (token.Length == 0)
        {
            previousUnconvertedShort = null;
            if (separator != " ") Reset();
            return new("", "", false, 0, "empty", 0);
        }
        DecisionResult Evaluate(string text) => !LayoutEnabled ? new(false, text, text, 0, "layout-disabled") :
            legacy ? (baselineEvaluator?.Invoke(text, exceptions) ?? oldEngine.Evaluate(text, exceptions)) :
            engine.Evaluate(text, context.LastOrDefault(), context.Count > 1 ? context[^2] : null, exceptions);
        var decision = Evaluate(token);
        if (decision.Reason == "exception")
        {
            Reset();
            return new(token, token, false, 0, "exception", 0);
        }
        string tail = "";
        string head = "";
        bool TargetKnown(DecisionResult value)
        {
            if (!value.ShouldConvert) return false;
            bool russian = value.Converted.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
            return engine.IsKnownWord(value.Converted, russian);
        }
        // A physical comma/dot can be a Russian letter, but punctuation after
        // an ordinary word is much more common. Keep it as punctuation unless
        // the complete converted form is a known word.
        if (!TargetKnown(decision))
        {
            int end = token.Length;
            while (end > 0 && ",.;:!?\"'»)]}".Contains(token[end - 1])) end--;
            if (end > 0 && end < token.Length)
            {
                decision = Evaluate(token[..end]);
                tail = token[end..];
            }
        }
        if (!TargetKnown(decision) && token.Length > 1)
        {
            int start = 0;
            while (start < token.Length - tail.Length && "\"'[{<".Contains(token[start])) start++;
            if (start > 0 && start < token.Length - tail.Length)
            {
                var inner = Evaluate(token[start..(token.Length - tail.Length)]);
                if (inner.ShouldConvert) { head = token[..start]; decision = inner; }
            }
        }
        string replacement = decision.ShouldConvert ? head + decision.Converted + tail : token;
        string reason = decision.Reason;
        double confidence = decision.Confidence;
        bool typoChanged = false;
        if (!legacy && TypoEnabled &&
            exceptions?.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)) != true &&
            !token.Any(c => char.IsDigit(c) || "@/\\_+=#%-".Contains(c)))
        {
            int start = 0, end = replacement.Length;
            while (start < end && "\"'([{<«".Contains(replacement[start])) start++;
            while (end > start && ",.;:!?\"'»)]}>".Contains(replacement[end - 1])) end--;
            if (start < end)
            {
                var allTypoExceptions = exceptions is null ? TypoExceptions :
                    TypoExceptions is null ? exceptions : exceptions.Concat(TypoExceptions);
                // The context is reset at a sentence end, Enter and Tab: an empty context is a sentence start.
                var typoDecision = typo.Evaluate(replacement[start..end], context.LastOrDefault(),
                    context.Count > 1 ? context[^2] : null, allTypoExceptions, TypoPolicy.Auto, context.Count == 0);
                if (typoDecision.ShouldCorrect)
                {
                    replacement = replacement[..start] + typoDecision.Corrected + replacement[end..];
                    typoChanged = true;
                    reason = typoDecision.Reason;
                    confidence = typoDecision.Confidence;
                }
            }
        }
        int previousCharacters = 0;
        string original = token;
        if (!legacy && decision.ShouldConvert && previousUnconvertedShort is { Length: > 0 and <= 3 } shortWord &&
            SameScript(shortWord, token))
        {
            var retro = engine.Evaluate(shortWord, decision.Converted, null, exceptions);
            if (retro.ShouldConvert)
            {
                previousCharacters = shortWord.Length + 1;
                original = shortWord + " " + token;
                replacement = retro.Converted + " " + replacement;
                if (context.Count > 0) context[^1] = retro.Converted;
            }
        }
        context.Add(replacement.TrimEnd(',', '.', ';', ':', '!', '?'));
        if (context.Count > 2) context.RemoveAt(0);
        previousUnconvertedShort = separator == " " && !decision.ShouldConvert &&
            token.Length <= 3 && token.All(char.IsLetter) ? token : null;
        if (separator != " " || tail.IndexOfAny(['.', '!', '?']) >= 0) Reset();
        return new(original, replacement, decision.ShouldConvert || typoChanged, previousCharacters, reason, confidence);
    }
    private static bool SameScript(string a, string b) =>
        (a.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')) ==
        (b.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'));
}
