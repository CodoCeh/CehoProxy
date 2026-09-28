using System.Net;
using System.Net.Http;

namespace ProxyCage.Core;

public sealed class PanelLink : IDisposable
{
    public enum Outcome { Ok, NeedPassword, Waiting, Unreachable }

    public sealed record Reading(Outcome Outcome, TrayState.Snapshot? Snapshot, int WaitSeconds);

    private const int TooManyRequests = 429;

    private readonly string _root;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        CookieContainer = new CookieContainer(),
        UseCookies = true,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public PanelLink(string root) => _root = root;

    public string? Password { get; set; }

    public int? Port => Auth.ReadPanelPointer(_root);

    public string? Url => Port is { } port ? $"http://127.0.0.1:{port}" : null;

    public async Task<Reading> StatusAsync(bool quick)
    {
        var argv = quick ? TrayState.StatusArgs : TrayState.FullStatusArgs;
        var (code, body, retryAfter) = await ApiAsync(argv);
        return code switch
        {
            (int)HttpStatusCode.OK => new Reading(
                Outcome.Ok, TrayState.Parse(TrayState.Unwrap(body), !quick), 0),
            (int)HttpStatusCode.Unauthorized => new Reading(Outcome.NeedPassword, null, 0),
            TooManyRequests => new Reading(
                Outcome.Waiting, null, TrayState.RetryAfterSeconds(retryAfter)),
            _ => new Reading(Outcome.Unreachable, null, 0),
        };
    }

    public async Task<string?> LanguageAsync()
    {
        var (code, body, _) = await ApiAsync(new[] { "lang" });
        if (code != (int)HttpStatusCode.OK) return null;
        var text = TrayState.Unwrap(body)?.Trim();
        return string.IsNullOrEmpty(text) ? null : Strings.Normalize(text);
    }

    public Task<bool> TurnOnAsync() => ControlAsync("/control/start");

    public Task<bool> TurnOffAsync() => ControlAsync("/control/stop");

    private async Task<bool> ControlAsync(string path)
    {
        if (Url is not { } url) return false;
        if (Password is { Length: > 0 } && !await SignInAsync(url)) return false;
        return await FormAsync(url + path, new[]
        {
            new KeyValuePair<string, string>("tab", "state"),
        });
    }

    private Task<bool> SignInAsync(string url) => FormAsync(url + "/login", new[]
    {
        new KeyValuePair<string, string>("password", Password ?? ""),
    });

    private async Task<bool> FormAsync(string url, KeyValuePair<string, string>[] fields)
    {
        try
        {
            using var form = new FormUrlEncodedContent(fields);
            using var response = await _http.PostAsync(url, form);
            return response.StatusCode == HttpStatusCode.SeeOther;
        }
        catch { return false; }
    }

    private async Task<(int Code, string Body, string? RetryAfter)> ApiAsync(IReadOnlyList<string> argv)
    {
        if (Url is not { } url) return (0, "", null);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url + "/api")
            {
                Content = new StringContent(
                    string.Join("\n", argv), System.Text.Encoding.UTF8, "text/plain"),
            };
            var password = Password ?? "";
            if (Auth.HeaderSafe(password))
                request.Headers.TryAddWithoutValidation(Auth.PasswordHeader, password);
            request.Headers.TryAddWithoutValidation(Auth.PasswordHeaderBase64, Auth.Encode(password));
            using var response = await _http.SendAsync(request);
            var retryAfter = response.Headers.TryGetValues("Retry-After", out var values)
                ? values.FirstOrDefault()
                : null;
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(), retryAfter);
        }
        catch { return (0, "", null); }
    }

    public void Dispose() => _http.Dispose();
}
