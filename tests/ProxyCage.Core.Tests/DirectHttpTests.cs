using System.Net.Http;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class DirectHttpTests
{
    [Fact]
    public void CreateClient_sets_user_agent_and_timeout()
    {
        using var http = DirectHttp.CreateClient(TimeSpan.FromSeconds(15));
        Assert.Equal(TimeSpan.FromSeconds(15), http.Timeout);
        Assert.Contains("CehoProxy", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public async Task Resolves_github_api_host()
    {
        using var http = DirectHttp.CreateClient(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync(
            "https://api.github.com/repos/CodoCeh/CehoProxy/releases/latest",
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("tag_name", json);
    }
}
