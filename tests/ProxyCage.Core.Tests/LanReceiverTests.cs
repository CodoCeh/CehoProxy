using System.Net.Http;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class LanReceiverTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("/p/0123456789abcdef0123456789abcdef", true)]
    [InlineData("/p/0123456789abcdef0123456789abcdef?x=1", true)]
    [InlineData("/p/0123456789abcdef0123456789abcdee", false)]
    [InlineData("/p/0123456789abcdef", false)]
    [InlineData("/0123456789abcdef0123456789abcdef", false)]
    [InlineData("/", false)]
    public void Only_the_exact_secret_path_is_accepted(string path, bool expected)
        => Assert.Equal(expected, LanReceiver.TokenMatches(path, Token));

    [Fact]
    public void The_android_json_gives_subscription_links_and_falls_back_to_server_links()
    {
        var withSubs = "{\"format\":\"cehoproxy-share-v1\",\"profile\":{\"nodes\":[{\"raw\":\"vless://a@x:1\"}],\"subscriptionUrls\":[\"https://s.example/a\",\"https://s.example/a\"]}}";
        var ownOnly = "{\"format\":\"cehoproxy-share-v1\",\"profile\":{\"nodes\":[{\"raw\":\"vless://a@x:1\"},{\"raw\":\"trojan://p@y:2\"}],\"subscriptionUrls\":[]}}";

        Assert.Equal(new[] { "https://s.example/a" }, LanReceiver.ExtractLinks(withSubs));
        Assert.Equal(new[] { "vless://a@x:1", "trojan://p@y:2" }, LanReceiver.ExtractLinks(ownOnly));
    }

    [Fact]
    public void Plain_text_keeps_only_links_and_garbage_gives_nothing()
    {
        Assert.Equal(new[] { "https://s.example/a", "vless://a@x:1" }, LanReceiver.ExtractLinks("https://s.example/a\nпросто слова\r\nvless://a@x:1"));
        Assert.Empty(LanReceiver.ExtractLinks("{not json"));
        Assert.Empty(LanReceiver.ExtractLinks("слова без ссылок"));
    }

    [Fact]
    public void The_form_field_is_decoded_like_the_phone_page_sends_it()
        => Assert.Equal("https://s.example/a b", LanReceiver.FormField("x=1&link=https%3A%2F%2Fs.example%2Fa+b", "link"));

    [Fact]
    public async Task A_phone_can_post_once_and_the_door_closes()
    {
        using var receiver = new LanReceiver("test");
        if (!receiver.Start()) return;

        using var http = new HttpClient();
        var page = await http.GetAsync(receiver.Url);
        Assert.Equal(System.Net.HttpStatusCode.OK, page.StatusCode);
        var wrong = await http.GetAsync(receiver.Url[..^1] + (receiver.Url[^1] == '0' ? "1" : "0"));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, wrong.StatusCode);

        var post = await http.PostAsync(receiver.Url, new StringContent("https://s.example/a"));
        Assert.Equal(System.Net.HttpStatusCode.OK, post.StatusCode);
        Assert.Equal("https://s.example/a", receiver.Payload);
        await Task.Delay(300);
        Assert.False(receiver.Active);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetAsync(receiver.Url));
    }
}
