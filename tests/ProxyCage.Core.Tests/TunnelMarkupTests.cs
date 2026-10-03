using System.Text;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class TunnelMarkupTests
{
    private static string Render(string language = "en", string? picked = null, params AppEntry[] apps)
    {
        var cfg = new CehoConfig { Language = language, Apps = apps.ToList() };
        var sb = new StringBuilder();
        TunnelUi.Render(sb, cfg, [new("Notes <script>", "/opt/notes/notes", "fixture")], "apps", true, false, false, picked);
        return sb.ToString();
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Tunnel_catalog_and_manual_controls_are_present_without_javascript(string language)
    {
        var html = Render(language);
        Assert.True(html.IndexOf("class=tunnel-panel", StringComparison.Ordinal) < html.IndexOf("class=tunnel-catalog", StringComparison.Ordinal));
        Assert.Contains("id=tunnel-stage", html);
        Assert.Contains("id=tunnel-search", html);
        Assert.Contains("href=/apps/pick", html);
        Assert.Contains("name=intent value=tunnel", html);
        Assert.Contains("name=confirm_add value=1", html);
        Assert.Contains("name=confirm_apply value=1", html);
        Assert.Contains("role=status aria-live=polite", html);
        Assert.DoesNotContain("data-live=", html); // A poll must never replace a drag target or selected candidate.
        Assert.DoesNotContain("type=file", html);
    }

    [Fact]
    public void Catalog_names_and_authoritative_picker_paths_are_html_encoded()
    {
        var path = "/tmp/a\"/><script>bad</script>";
        var html = Render(picked: path);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("Notes &lt;script&gt;", html);
        Assert.Contains("id=tunnel-picked-form", html);
        Assert.Contains("/tmp/a&quot;/&gt;&lt;script&gt;bad&lt;/script&gt;", html);
    }

    [Fact]
    public void Existing_exact_identity_remains_in_catalog_with_focus_target()
    {
        var app = new AppEntry { Name = "Notes", Folder = "/opt/notes", IdentityPath = "/opt/notes/notes" };
        var html = Render(apps: [app]);
        Assert.Contains($"data-existing-id=\"{AppIdentity.Id(app)}\"", html);
        Assert.Contains("Already added: Notes", html);
        Assert.Contains("data-tunnel-source", html);
    }

    [Fact]
    public void Folder_coverage_does_not_mark_an_unrelated_executable_as_exact_duplicate()
    {
        var app = new AppEntry { Name = "Other", Folder = "/opt/notes", IdentityPath = "/opt/notes/other" };
        var html = Render(apps: [app]);
        Assert.DoesNotContain("data-existing-id", html);
        Assert.Contains("Add to VPN: Notes", html);
    }

    [Fact]
    public void Visual_motion_is_bounded_and_respects_reduced_motion()
    {
        Assert.Contains("@media(prefers-reduced-motion:reduce)", TunnelUi.Css);
        Assert.Contains("[data-phase=applied] .tunnel-token{animation:tunnel-enter .55s", TunnelUi.Css);
        Assert.DoesNotContain("infinite", TunnelUi.Css);
        Assert.Contains("filter:none!important", TunnelUi.Css); // Catalog app colors are unmodified.
    }
}
