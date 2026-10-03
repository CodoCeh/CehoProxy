using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class SupportReportTests
{
    [Fact]
    public void All_sections_omit_secrets_links_addresses_paths_and_arbitrary_log_text()
    {
        var cfg = new CehoConfig { PasswordHash = "password-hash-private", PasswordSalt = "salt-private" };
        cfg.Subscriptions.Add(new SubscriptionEntry
        {
            Name = "private-subscription-name", Url = "https://secret.example.test/private-token",
            LastError = "TLS certificate error with token super-secret at /home/alex/private-file",
        });
        cfg.Apps.Add(new AppEntry { Name = "private-app-name", Folder = @"C:\Users\Alex\private", Launch = "/home/alex/private/app" });
        cfg.DirectSites.Add("private-domain.example.test");
        cfg.BlockedNodes.Add("Vless|198.51.100.99|443|private-uuid");
        var logs = new[] {
            "TLS certificate invalid https://secret.example.test/private-token", "Password: plain-password",
            "unkeyed-super-secret-value", "192.0.2.11 [2001:db8::1] /home/alex/private C:\\Users\\Alex\\private",
        };
        var report = SupportReport.Create(cfg, SupportReport.Parts.All,
            new WebServer.ControlState(true, "private-country", "203.0.113.7", "unkeyed-super-secret-value", true), logs: logs);
        foreach (var forbidden in new[] { "password-hash-private", "salt-private", "private-subscription-name", "secret.example.test",
            "private-token", "super-secret", "private-app-name", "private-domain", "198.51.100.99", "private-uuid", "203.0.113.7", "192.0.2.11", "2001:db8", "Alex", "/home/alex" })
            Assert.DoesNotContain(forbidden, report);
        Assert.Contains("TLS / certificate verification", report);
        Assert.Contains("Events: 4", report);
    }

    [Fact]
    public void Selection_is_respected_and_logs_are_opt_in()
    {
        Assert.False(SupportReport.DefaultParts.HasFlag(SupportReport.Parts.Logs));
        var selected = SupportReport.PartsFromForm(new Dictionary<string, string> { ["report_environment"] = "1" });
        Assert.Equal(SupportReport.Parts.Environment, selected);
        var report = SupportReport.Create(new CehoConfig(), selected, logs: new[] { "test" });
        Assert.Contains("[Environment]", report);
        Assert.DoesNotContain("[Configuration summary]", report);
        Assert.DoesNotContain("[Diagnostics]", report);
        Assert.DoesNotContain("[Recent event summary]", report);
        Assert.Equal(SupportReport.Parts.None, SupportReport.PartsFromForm(new Dictionary<string, string>()));
    }

    [Fact]
    public void Preview_is_encoded_local_only_and_downloads_exact_reviewed_text()
    {
        var report = "<script>alert('unsafe')</script>\npreview body";
        var html = SafetyPanel.RenderReportPreview(new CehoConfig { Language = "en" }, report, SupportReport.Parts.Environment);
        Assert.DoesNotContain(report, html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("action=/support/download", html);
        Assert.Contains("name=report", html);
        Assert.Contains(WebUi.ThemeEarlyScript, html);
        Assert.Contains("Nothing has been sent", html);
        Assert.DoesNotContain("fetch(", html);
    }

    [Fact]
    public void Insecure_direct_node_is_detected_without_changing_it()
    {
        var sub = new SubscriptionEntry { Url = "naive://user:fake-password@example.test:443?insecure=1" };
        // BuildUri uses the parser's supported canonical insecure flag.
        sub.Url = NaiveProxyHelper.BuildUri(new NaiveProxySettings
            { Enabled = true, Server = "example.test", Username = "user", Password = "fake-password", AllowInsecure = true });
        Assert.True(SupportReport.HasInsecureTls(sub));
        var canonical = sub.Url;
        _ = SupportReport.Create(new CehoConfig { Subscriptions = new() { sub } });
        Assert.Equal(canonical, sub.Url);
    }
}
