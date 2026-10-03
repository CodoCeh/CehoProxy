using System.Reflection;
using System.Text;
using System.Xml.Linq;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class ProductIdentityTests
{
    private static XDocument Svg(string uri)
    {
        Assert.StartsWith("data:image/svg+xml", uri);
        var comma = uri.IndexOf(',');
        Assert.True(comma > 0);
        var content = uri[..comma].EndsWith(";base64", StringComparison.Ordinal)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(uri[(comma + 1)..]))
            : Uri.UnescapeDataString(uri[(comma + 1)..]);
        return XDocument.Parse(content);
    }

    [Fact]
    public void Product_identity_is_vector_and_distinct_from_company_mark()
    {
        Assert.NotEqual(Brand.LogoDataUri, ProductBrand.LogoDataUri);
        Assert.StartsWith("data:image/png", Brand.LogoDataUri);
        Assert.Equal("https://codoceh.ru", Brand.Site);
        foreach (var uri in new[] { ProductBrand.LogoDataUri, ProductBrand.DarkLogoDataUri, ProductBrand.IconDataUri })
        {
            var document = Svg(uri);
            Assert.Equal("svg", document.Root!.Name.LocalName);
            Assert.NotNull(document.Root.Attribute("viewBox"));
            Assert.DoesNotContain(document.Descendants(), e => e.Name.LocalName is "script" or "image");
        }
    }

    [Fact]
    public void Header_and_login_share_light_dark_product_mark_and_product_accessible_name()
    {
        var output = new StringBuilder();
        typeof(WebServer).GetMethod("AppendProductLogo", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { output });
        var html = output.ToString();
        Assert.Contains(ProductBrand.LogoDataUri, html);
        Assert.Contains(ProductBrand.DarkLogoDataUri, html);
        Assert.Contains("alt=\"CehoProxy\"", html);
        Assert.DoesNotContain(Brand.LogoDataUri, html);
        Assert.Contains("logo-light", html);
        Assert.Contains("logo-dark", html);
    }

    [Fact]
    public void Page_head_includes_matching_product_favicon_and_manual_theme_support()
    {
        var output = new StringBuilder();
        typeof(WebServer).GetMethod("Head", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { output, new CehoConfig(), null });
        Assert.Contains("rel=icon", output.ToString());
        Assert.Contains(ProductBrand.IconDataUri, output.ToString());
        Assert.Contains(":root[data-theme=dark] .product-logo", WebUi.Css);
        Assert.Contains(":root:not([data-theme=light]) .product-logo", WebUi.Css);
    }

    [Fact]
    public void Footer_retains_exact_company_credit_and_verified_project_destinations()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-product-footer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var cfg = new CehoConfig { Language = "ru" };
            var path = Path.Combine(root, "config.json");
            cfg.Save(path);
            var state = new WebServer.ControlState(false, null, null, null, false);
            var web = new WebServer(path, () => state, _ => { });
            var html = (string)typeof(WebServer).GetMethod("RenderPage", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(web, new object?[] { cfg, state, "help", null, false, null, LogView.All, null, null, null })!;
            var footer = html[html.IndexOf("<footer>", StringComparison.Ordinal)..html.IndexOf("</footer>", StringComparison.Ordinal)];
            Assert.Contains("href=\"https://codoceh.ru\"", footer);
            Assert.Contains(System.Net.WebUtility.HtmlEncode("Выковано в КодоЦех"), footer);
            Assert.Contains("href=\"https://github.com/CodoCeh/CehoProxy\"", footer);
            Assert.Contains("href=\"https://t.me/CodoCeh\"", footer);
            Assert.Contains(Brand.LogoDataUri, footer);
            Assert.DoesNotContain(ProductBrand.LogoDataUri, footer);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
