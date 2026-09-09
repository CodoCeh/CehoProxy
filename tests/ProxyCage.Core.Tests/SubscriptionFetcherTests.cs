using System.Net;
using System.Net.Http.Headers;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class SubscriptionFetcherTests
{
    [Theory]
    [InlineData(1, SubscriptionFetchPersona.SingBox)]
    [InlineData(2, SubscriptionFetchPersona.Clash)]
    [InlineData(3, SubscriptionFetchPersona.Client)]
    [InlineData(4, SubscriptionFetchPersona.Browser)]
    [InlineData(5, SubscriptionFetchPersona.SingBox)]
    public void First_attempt_mimics_sing_box_client(int attempt, SubscriptionFetchPersona expected)
    {
        Assert.Equal(expected, SubscriptionFetcher.PersonaOf(attempt));
    }

    [Fact]
    public void SingBox_persona_sends_exclave_like_user_agent_and_hwid()
    {
        var headers = new HttpRequestMessage().Headers;
        SubscriptionFetcher.ApplyPersonaHeaders(
            headers, SubscriptionFetchPersona.SingBox, "1.2.33", "cehoa1b2c3d4e5f67890");

        Assert.Contains("SFA/", Flat(headers, "User-Agent"));
        Assert.Contains("sing-box", Flat(headers, "User-Agent"));
        Assert.Equal("*/*", Flat(headers, "Accept"));
        Assert.Equal("cehoa1b2c3d4e5f67890", Flat(headers, "x-hwid"));
        Assert.NotEqual("CehoProxy", Flat(headers, "x-device-model"));
    }

    [Fact]
    public void Client_persona_uses_wildcard_accept_not_text_plain_only()
    {
        var headers = new HttpRequestMessage().Headers;
        SubscriptionFetcher.ApplyPersonaHeaders(
            headers, SubscriptionFetchPersona.Client, "1.2.33", "cehoa1b2c3d4e5f67890");

        Assert.Contains("CehoProxy/1.2.33", Flat(headers, "User-Agent"));
        Assert.Equal("*/*", Flat(headers, "Accept"));
    }

    [Fact]
    public void Hwid_limit_on_404_is_not_reported_as_generic_http_error()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        response.Headers.TryAddWithoutValidation("x-hwid-max-devices-reached", "true");

        var message = SubscriptionFetcher.HwidFailureMessage(response, null, "ru");
        Assert.Equal(Strings.T("ru", "sub_hwid_limit"), message);
    }

    [Fact]
    public void Hwid_gate_on_404_with_active_flag()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        response.Headers.TryAddWithoutValidation("x-hwid-active", "true");
        response.Headers.TryAddWithoutValidation("x-hwid-not-supported", "true");

        var message = SubscriptionFetcher.HwidFailureMessage(response, null, "en");
        Assert.Equal(Strings.T("en", "sub_hwid_gate"), message);
    }

    [Fact]
    public async Task Mock_handler_receives_sing_box_headers_and_body_is_parsed()
    {
        var body = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "fixtures", "sub-caddy-naive.json"));
        string? seenUserAgent = null;
        string? seenHwid = null;

        var handler = new StubHandler((request, _) =>
        {
            seenUserAgent = request.Headers.UserAgent.ToString();
            seenHwid = request.Headers.TryGetValues("x-hwid", out var hwid)
                ? hwid.FirstOrDefault()
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        });

        using var http = new HttpClient(handler);
        SubscriptionFetcher.ApplyPersonaHeaders(
            http.DefaultRequestHeaders, SubscriptionFetchPersona.SingBox, "1.2.33", "cehoa1b2c3d4e5f67890");

        using var response = await http.GetAsync("https://example.test/sub");
        var text = await response.Content.ReadAsStringAsync();
        var nodes = SubscriptionParser.Parse(text);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("SFA/", seenUserAgent);
        Assert.Equal("cehoa1b2c3d4e5f67890", seenHwid);
        var node = Assert.Single(nodes);
        Assert.Equal(ProxyProtocol.Naive, node.Protocol);
        Assert.Equal("site.roomspace.team", node.Server);
    }

    private static string Flat(HttpRequestHeaders headers, string name) =>
        string.Join(", ", headers.Where(h =>
            h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).SelectMany(h => h.Value));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
