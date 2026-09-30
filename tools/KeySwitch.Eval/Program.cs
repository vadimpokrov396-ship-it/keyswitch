using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using KeySwitch.Core;

if (args.Length != 3) throw new ArgumentException("Usage: KeySwitch.Eval heldout_sentences.tsv report.json baseline/KeySwitch.Core.dll");
var engine = new DecisionEngine();
var baselineAssembly = Assembly.LoadFile(Path.GetFullPath(args[2]));
var baselineType = baselineAssembly.GetType("KeySwitch.Core.DecisionEngine") ?? throw new InvalidOperationException("Baseline DecisionEngine missing");
var baselineInstance = Activator.CreateInstance(baselineType) ?? throw new InvalidOperationException("Baseline instance missing");
var baselineMethod = baselineType.GetMethod("Evaluate") ?? throw new InvalidOperationException("Baseline Evaluate missing");
var defaults = baselineMethod.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
DecisionResult Baseline(string token, IEnumerable<string>? exceptions)
{
    object?[] parameters = (object?[])defaults.Clone();
    parameters[0] = token;
    if (parameters.Length > 1) parameters[1] = exceptions;
    object value = baselineMethod.Invoke(baselineInstance, parameters) ?? throw new InvalidOperationException("Null baseline decision");
    Type type = value.GetType();
    T Get<T>(string name) => (T)(type.GetProperty(name)?.GetValue(value) ?? throw new InvalidOperationException("Missing baseline result field " + name));
    return new(Get<bool>("ShouldConvert"), token, Get<string>("Converted"), Get<double>("Confidence"), Get<string>("Reason"));
}
var results = new Dictionary<string, Counter>();
foreach (var row in File.ReadLines(args[0]))
{
    int tab = row.IndexOf('\t');
    if (tab < 0) continue;
    string source = row[..tab], sentence = row[(tab + 1)..];
    string direction = source.StartsWith("rus_") ? "ru_to_en_keys" : "en_to_ru_keys";
    foreach (bool legacy in new[] { true, false })
    foreach (bool wrong in new[] { false, true })
    {
        string key = $"{(legacy ? "old" : "new")}/{direction}/{(wrong ? "wrong" : "correct")}";
        if (!results.TryGetValue(key, out var counter)) results[key] = counter = new();
        string input = wrong ? Regex.Replace(sentence, @"\p{L}+", m => LayoutMap.Convert(m.Value)) : sentence;
        // A final physical Space is required to trigger the last token in the live app.
        string output = Replay(input + " ", new BoundaryEngine(engine, legacy, legacy ? Baseline : null), counter);
        string[] expectedWords = Regex.Split(sentence.Trim(), @"\s+");
        string[] inputWords = Regex.Split(input.Trim(), @"\s+");
        string[] outputWords = Regex.Split(output.Trim(), @"\s+");
        int count = expectedWords.Length;
        counter.Sentences++;
        counter.AllWords += count;
        for (int i = 0; i < count; i++)
        {
            string expected = expectedWords[i];
            string typed = i < inputWords.Length ? inputWords[i] : "";
            string emitted = i < outputWords.Length ? outputWords[i] : "";
            if (wrong)
            {
                if (typed == expected) { counter.UnchangedByLayout++; continue; }
                counter.WrongWords++;
                if (emitted == expected) counter.Corrected++;
                else
                {
                    if (emitted != typed) counter.WrongChangedIncorrectly++;
                    if (counter.MissExamples.Count < 40) counter.MissExamples.Add($"{typed} -> {emitted} expected {expected}");
                }
            }
            else
            {
                counter.CorrectWords++;
                if (emitted != expected)
                {
                    counter.FalseConversions++;
                    if (counter.FalseExamples.Count < 40) counter.FalseExamples.Add($"{typed} -> {emitted}");
                }
            }
        }
        if (outputWords.Length != count) counter.MisalignedSentences++;
    }
}
var report = results.ToDictionary(x => x.Key, x => x.Value.ToReport());
foreach (var implementation in new[] { "old", "new" })
foreach (var direction in new[] { "ru_to_en_keys", "en_to_ru_keys" })
{
    var wrong = results[$"{implementation}/{direction}/wrong"];
    var correct = results[$"{implementation}/{direction}/correct"];
    int falsePositives = wrong.WrongChangedIncorrectly + correct.FalseConversions;
    report[$"{implementation}/{direction}/summary"] = new {
        Corrected = wrong.Corrected, WrongWords = wrong.WrongWords,
        Recall = wrong.WrongWords == 0 ? 0 : (double)wrong.Corrected / wrong.WrongWords,
        FalseConversions = correct.FalseConversions, CorrectWords = correct.CorrectWords,
        FalseConversionRate = correct.CorrectWords == 0 ? 0 : (double)correct.FalseConversions / correct.CorrectWords,
        WrongChangedIncorrectly = wrong.WrongChangedIncorrectly,
        Precision = wrong.Corrected + falsePositives == 0 ? 0 : (double)wrong.Corrected / (wrong.Corrected + falsePositives)
    };
}
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(results.ToDictionary(x => x.Key, x => new { x.Value.Sentences, x.Value.WrongWords,
    x.Value.Corrected, Recall = x.Value.WrongWords == 0 ? 0 : (double)x.Value.Corrected / x.Value.WrongWords,
    x.Value.CorrectWords, x.Value.FalseConversions,
    FalseConversionRate = x.Value.CorrectWords == 0 ? 0 : (double)x.Value.FalseConversions / x.Value.CorrectWords }),
    new JsonSerializerOptions { WriteIndented = true }));

static string Replay(string input, BoundaryEngine boundary, Counter counter)
{
    var emitted = new StringBuilder(input.Length + 8);
    var token = new StringBuilder();
    var watch = Stopwatch.StartNew();
    void Complete(string separator)
    {
        string original = token.ToString();
        token.Clear();
        var decision = boundary.Complete(original, separator);
        if (decision.Changed)
        {
            int erase = original.Length + decision.PreviousCharacters;
            if (erase > emitted.Length) throw new InvalidOperationException("Retroactive erase crosses sentence start");
            emitted.Length -= erase;
            emitted.Append(decision.Replacement);
        }
        emitted.Append(separator);
        if (original.Length > 0)
        {
            counter.Decisions++;
            counter.ReasonCounts[decision.Reason] = counter.ReasonCounts.GetValueOrDefault(decision.Reason) + 1;
        }
    }
    foreach (char c in input)
    {
        if (BoundaryEngine.IsTokenCharacter(c)) { token.Append(c); emitted.Append(c); }
        else Complete(c.ToString());
    }
    Complete("");
    watch.Stop();
    counter.ElapsedMs += watch.Elapsed.TotalMilliseconds;
    return emitted.ToString();
}

sealed class Counter
{
    public int Sentences, AllWords, UnchangedByLayout, WrongWords, Corrected, WrongChangedIncorrectly, CorrectWords, FalseConversions, MisalignedSentences, Decisions;
    public double ElapsedMs;
    public Dictionary<string, int> ReasonCounts = new();
    public List<string> MissExamples = new(), FalseExamples = new();
    public object ToReport() => new { Sentences, AllWords, UnchangedByLayout, WrongWords, Corrected, WrongChangedIncorrectly,
        Recall = WrongWords == 0 ? 0 : (double)Corrected / WrongWords,
        CorrectWords, FalseConversions,
        FalseConversionRate = CorrectWords == 0 ? 0 : (double)FalseConversions / CorrectWords,
        MisalignedSentences, Decisions, ElapsedMs, MeanDecisionMs = Decisions == 0 ? 0 : ElapsedMs / Decisions,
        ReasonCounts, MissExamples, FalseExamples };
}
