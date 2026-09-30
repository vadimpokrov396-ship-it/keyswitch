using System.Text.Json;
using System.Text.RegularExpressions;
using KeySwitch.Core;

if (args.Length != 2) throw new ArgumentException("Usage: KeySwitch.TypoEval heldout_sentences.tsv output.json");
var layout = new DecisionEngine();
var corrector = new TypoCorrector(layout);
var results = new Dictionary<string, Counts> { ["ru"] = new(), ["en"] = new() };
var regex = new Regex(@"(?<![\p{L}'’@/._-])\p{L}{4,32}(?![\p{L}'’@/._-])", RegexOptions.Compiled);
int sentenceNumber = 0;
foreach (var line in File.ReadLines(args[0]))
{
    int tab = line.IndexOf('\t');
    if (tab < 0) continue;
    var language = line.StartsWith("rus_", StringComparison.Ordinal) ? "ru" : "en";
    var counts = results[language];
    // Fixed, disjoint 1000-sentence sample per language from the v1.1 final holdout.
    if (counts.Sentences >= 1000) continue;
    counts.Sentences++;
    var sentence = line[(tab + 1)..];
    string? previous = null, previous2 = null;
    bool injected = false;
    foreach (Match match in regex.Matches(sentence))
    {
        var word = match.Value;
        bool ru = language == "ru";
        if (word.Any(char.IsUpper) || !word.All(c => ru ? c is >= 'а' and <= 'я' or 'ё' : c is >= 'a' and <= 'z')) continue;
        counts.CleanWords++;
        var clean = corrector.Evaluate(word, previous, previous2);
        if (clean.ShouldCorrect)
        {
            counts.FalseCorrections++;
            if (counts.FalseExamples.Count < 20) counts.FalseExamples.Add($"{word} -> {clean.Corrected}");
        }
        if (!injected && layout.IsKnownWord(word, ru))
        {
            // Reproducible keyboard-like edit: transposition, omission, or a neighbouring key.
            var typo = MakeTypo(word, ru, sentenceNumber);
            if (typo != word && !layout.IsKnownWord(typo, ru))
            {
                injected = true;
                counts.Typos++;
                var result = corrector.Evaluate(typo, previous, previous2);
                if (result.Corrected == word) counts.Corrected++;
                else if (result.ShouldCorrect) counts.WrongCorrections++;
                if (counts.MissExamples.Count < 20 && result.Corrected != word)
                    counts.MissExamples.Add($"{word} -> {typo} -> {result.Corrected}");
            }
        }
        previous2 = previous;
        previous = word;
    }
    sentenceNumber++;
}
var report = results.ToDictionary(x => x.Key, x => new {
    x.Value.Sentences, x.Value.CleanWords, x.Value.FalseCorrections,
    FalseCorrectionRate = (double)x.Value.FalseCorrections / Math.Max(1, x.Value.CleanWords),
    x.Value.Typos, x.Value.Corrected, x.Value.WrongCorrections,
    Recall = (double)x.Value.Corrected / Math.Max(1, x.Value.Typos),
    Precision = (double)x.Value.Corrected / Math.Max(1, x.Value.Corrected + x.Value.WrongCorrections),
    x.Value.FalseExamples, x.Value.MissExamples
});
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(args[1], json);
Console.WriteLine(json);

static string MakeTypo(string word, bool ru, int seed)
{
    int index = 1 + (seed % (word.Length - 2));
    if (seed % 3 == 0) return word.Remove(index, 1);
    if (seed % 3 == 1) return word[index] == word[index + 1] ? word.Remove(index, 1) :
        word[..index] + word[index + 1] + word[index] + word[(index + 2)..];
    string[] rows = ru ? ["йцукенгшщзхъ", "фывапролджэ", "ячсмитьбю"] : ["qwertyuiop", "asdfghjkl", "zxcvbnm"];
    string? row = rows.FirstOrDefault(x => x.Contains(word[index]));
    if (row is null) return word;
    int key = row.IndexOf(word[index]);
    char neighbour = row[key + (key + 1 < row.Length ? 1 : -1)];
    return word[..index] + neighbour + word[(index + 1)..];
}

sealed class Counts
{
    public int Sentences, CleanWords, FalseCorrections, Typos, Corrected, WrongCorrections;
    public List<string> FalseExamples = new(), MissExamples = new();
}
