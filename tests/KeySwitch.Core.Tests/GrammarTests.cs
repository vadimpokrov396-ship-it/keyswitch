using System.Net;
using KeySwitch.App;
using Xunit;

namespace KeySwitch.Core.Tests;

public sealed class GrammarTests
{
    [Fact]
    public async Task SendsOnlySelectedTextAndAppliesTopNonoverlappingSuggestions()
    {
        var handler = new StubHandler();
        using var client = new GrammarClient(handler, () => new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc));
        var result = await client.CheckAsync("I has apple.", "en-US");
        Assert.Equal("https://api.languagetool.org/v2/check", handler.Uri);
        Assert.Equal("text=I+has+apple.&language=en-US", handler.Body);
        Assert.Equal("I have apple.", result.Corrected);
        Assert.Equal(2, result.Suggestions);
    }

    [Fact]
    public async Task RateLimitStopsSecondRequestBeforeNetwork()
    {
        var handler = new StubHandler();
        using var client = new GrammarClient(handler, () => new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc));
        await client.CheckAsync("I has apple.", "en-US");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CheckAsync("another text", "en-US"));
        Assert.Equal(1, handler.Calls);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls;
        public string? Uri, Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"matches":[{"offset":2,"length":3,"replacements":[{"value":"have"},{"value":"had"}]},{"offset":2,"length":3,"replacements":[{"value":"had"}]}]}""")
            };
        }
    }
}
