using System.Net;
using System.Text;
using System.Text.Json;

namespace KeySwitch.App;

internal sealed record GrammarEdit(int Offset, int Length, string Replacement);
internal sealed record GrammarCheck(string Corrected, int Suggestions);

internal sealed class GrammarClient : IDisposable
{
    private const int MaxRequestBytes = 20_000;
    private const int MaxMinuteBytes = 70_000;
    private static readonly Uri Endpoint = new("https://api.languagetool.org/v2/check");
    private readonly HttpClient client;
    private readonly Func<DateTime> clock;
    private readonly Queue<(DateTime At, int Bytes)> requests = new();
    private DateTime lastRequest;
    private DateTime retryAfter;
    internal GrammarClient(HttpMessageHandler? handler = null, Func<DateTime>? clock = null)
    {
        client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(12);
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    internal async Task<GrammarCheck> CheckAsync(string selected, string language = "auto")
    {
        if (language is not ("auto" or "ru-RU" or "en-US")) throw new ArgumentException("Unsupported LanguageTool language", nameof(language));
        int bytes = Encoding.UTF8.GetByteCount(selected);
        if (bytes == 0 || bytes > MaxRequestBytes) throw new InvalidOperationException("Выделение должно быть не больше 20 КБ в UTF-8.");
        var now = clock();
        while (requests.Count > 0 && now - requests.Peek().At >= TimeSpan.FromMinutes(1)) requests.Dequeue();
        if (now < retryAfter || now - lastRequest < TimeSpan.FromSeconds(5) || requests.Count >= 18 || requests.Sum(x => x.Bytes) + bytes > MaxMinuteBytes)
            throw new InvalidOperationException("Лимит LanguageTool. Повторите проверку позже.");
        lastRequest = now;
        requests.Enqueue((now, bytes));
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = selected, ["language"] = language });
        using var response = await client.PostAsync(Endpoint, content);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            retryAfter = response.Headers.RetryAfter?.Date?.UtcDateTime ?? now + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            throw new InvalidOperationException("LanguageTool ограничил число запросов. Повторите позже.");
        }
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var edits = new List<GrammarEdit>();
        foreach (var match in document.RootElement.GetProperty("matches").EnumerateArray())
        {
            int offset = match.GetProperty("offset").GetInt32();
            int length = match.GetProperty("length").GetInt32();
            var replacements = match.GetProperty("replacements");
            if (replacements.GetArrayLength() == 0 || offset < 0 || offset > selected.Length || length < 0 || length > selected.Length - offset) continue;
            edits.Add(new GrammarEdit(offset, length, replacements[0].GetProperty("value").GetString() ?? ""));
        }
        string corrected = ApplySuggestions(selected, edits);
        return new GrammarCheck(corrected, edits.Count);
    }

    internal static string ApplySuggestions(string text, IEnumerable<GrammarEdit> edits)
    {
        int next = text.Length;
        var insertions = new HashSet<int>();
        foreach (var edit in edits.OrderByDescending(x => x.Offset))
        {
            if (edit.Offset < 0 || edit.Offset > next || edit.Length < 0 || edit.Length > next - edit.Offset) continue;
            if (edit.Length == 0 && !insertions.Add(edit.Offset)) continue;
            text = text.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Replacement);
            next = edit.Offset;
        }
        return text;
    }

    public void Dispose() => client.Dispose();
}
