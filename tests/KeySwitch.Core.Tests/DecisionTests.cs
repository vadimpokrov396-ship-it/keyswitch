using KeySwitch.Core;
using Xunit;
using Xunit.Abstractions;
namespace KeySwitch.Core.Tests;
public sealed class DecisionTests(ITestOutputHelper output)
{
    private static readonly DecisionEngine Engine = new();
    private static string[] Words(string language) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", language + ".txt")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
    public static IEnumerable<object[]> RealWords() => Words("en").Concat(Words("ru")).Select(w => new object[] { w });
    [Theory, MemberData(nameof(RealWords))]
    public void RealWordsAreNotChanged(string word) => Assert.False(Engine.Evaluate(word).ShouldConvert, word);
    public static IEnumerable<object[]> ProtectedTokens()
    {
        string[] samples = ["https://ghbdtn.com", "www.ghbdtn.ru", "ghbdtn@example.com", "fooBar", "someVariable", "my_identifier", "my-identifier", "ghbdtn123", "123", "12.34", "C:\\Users", "./hello", "hello/world", "git@github.com", "GHBD", "API", "SQL", "HTTP", "NASA", "РУДД", "testРуддщ", "hello мир", "rfr ltkf", "a", "I", "в", "и", "he", "on", "ok", "", " ", "🙂", "中文", "日本語", "123руддщ", "рут_руддщ", "myРуддщ", "руддщ.сщь", "файл.txt", "ghbdtn.org", "a+b", "a=b", "#ghbdtn", "in", "is", "it", "no", "to", "we", "me", "do", "go", "as", "my", "or", "if", "up", "by", "be", "да", "но", "на", "он", "вы", "мы", "то", "по", "от", "не", "за", "из", "же", "The", "Привет"];
        return samples.Select(w => new object[] { w });
    }
    [Theory, MemberData(nameof(ProtectedTokens))]
    public void StructuredTokensAreNotChanged(string token) => Assert.False(Engine.Evaluate(token).ShouldConvert, token);
    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("руддщ", "hello")]
    [InlineData("цщкдв", "world")]
    [InlineData("cgfcb,j", "спасибо")]
    [InlineData("rfr", "как")]
    [InlineData("ltkf", "дела")]
    [InlineData("pfgecnbk", "запустил")]
    [InlineData("ghjuhfvve", "программу")]
    [InlineData("htfkbpfwbb", "реализации")]
    [InlineData("rjvgm.nth", "компьютер")]
    public void RequiredExamplesConvert(string input, string expected)
    {
        var result = Engine.Evaluate(input);
        Assert.True(result.ShouldConvert, result.Reason);
        Assert.Equal(expected, result.Converted);
    }
    [Theory]
    [InlineData("ghbdtn")][InlineData("GHBDTN")][InlineData("Ghbdtn")]
    public void ExceptionsAreCaseInsensitive(string input) => Assert.False(Engine.Evaluate(input, ["ghbdtn"]).ShouldConvert);
    [Fact] public void CorrectedExceptionAlsoProtectsInput() => Assert.False(Engine.Evaluate("ghbdtn", ["привет"]).ShouldConvert);
    [Theory]
    [InlineData("привет", "ghbdtn")][InlineData("спасибо", "cgfcb,j")][InlineData("hello", "руддщ")]
    [InlineData("Привет", "Ghbdtn")][InlineData("ПРИВЕТ", "GHBDTN")][InlineData("ХЪЖЭБЮЁ", "{}:\"<>~")]
    public void PhysicalMappingWorks(string word, string encoded) { Assert.Equal(encoded, LayoutMap.Convert(word)); Assert.Equal(word, LayoutMap.Convert(encoded)); }
    [Fact]
    public void BenchmarkPrecisionAndRecall()
    {
        int tp = 0, fp = 0, tn = 0, fn = 0;
        var misses = new List<string>();
        foreach (var word in Words("en").Concat(Words("ru")).Where(w => w.Length >= 2))
        {
            var wrong = LayoutMap.Convert(word);
            var r = Engine.Evaluate(wrong);
            if (r.ShouldConvert && r.Converted == word) tp++;
            else { fn++; misses.Add(wrong + " -> " + word + " (" + r.Reason + ")"); if(r.ShouldConvert) fp++; }
        }
        foreach (var row in RealWords().Concat(ProtectedTokens())) { if (Engine.Evaluate((string)row[0]).ShouldConvert) fp++; else tn++; }
        double precision = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
        double recall = (double)tp / (tp + fn);
        string report = $"TP={tp} FP={fp} TN={tn} FN={fn}; precision={precision:P3}; recall={recall:P3}; total={tp+tn+fn+fp}";
        output.WriteLine(report);
        output.WriteLine(string.Join(Environment.NewLine, misses));
        string? path = Environment.GetEnvironmentVariable("KEYSWITCH_BENCHMARK_REPORT");
        if (path is not null) File.WriteAllText(path, report + Environment.NewLine + "Synthetic layout errors from curated word fixtures; not a production estimate." + Environment.NewLine + string.Join(Environment.NewLine, misses));
        Assert.True(precision >= .98, report);
        Assert.True(recall >= .75, report);
    }
}
