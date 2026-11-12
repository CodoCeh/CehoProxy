using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class HomeCountryTests
{
    private static CehoConfig Config(string home, bool offline = false)
    {
        var cfg = new CehoConfig { HomeCountry = home };
        cfg.Apps.Add(new AppEntry { Name = "homeapp", Folder = Os.IsWindows ? @"C:\Games\HomeApp" : "/tmp/HomeApp" });
        if (offline) cfg.Apps.Add(new AppEntry { Name = "offline", Folder = Os.IsWindows ? @"C:\Games\Offline" : "/tmp/Offline", NoInternet = true });
        return cfg;
    }

    private static JsonObject Generate(CehoConfig cfg, string dir)
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        return (JsonObject)JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg, null, dir))!;
    }

    [Fact]
    public void Russia_adds_domain_and_ip_sets_sent_direct_for_chosen_apps_only()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var json = Generate(Config("ru", offline: true), dir);
        var sets = json["route"]!["rule_set"]!.AsArray().Select(s => (string)s!["tag"]!).ToList();
        Assert.Equal(new[] { "country-ru-ip", "country-ru-domains" }.Order(), sets.Order());
        Assert.All(json["route"]!["rule_set"]!.AsArray(), s => Assert.True(File.Exists((string)s!["path"]!)));

        var rule = json["route"]!["rules"]!.AsArray().Single(r => r!["rule_set"] is not null)!;
        Assert.Equal("direct", (string)rule["outbound"]!);
        var regexes = rule["process_path_regex"]!.AsArray().Select(x => (string)x!).ToList();
        Assert.Contains(regexes, x => x.Contains("HomeApp"));
        Assert.DoesNotContain(regexes, x => x.Contains("Offline"));

        var dns = json["dns"]!["rules"]!.AsArray().Single(r => r!["rule_set"] is not null)!;
        Assert.Equal("dns-direct", (string)dns["server"]!);
        Assert.Equal("country-ru-domains", (string)dns["rule_set"]![0]!);
    }

    [Fact]
    public void Russia_sends_whole_russian_zones_and_listed_regional_sites_direct()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var json = Generate(Config("ru"), dir);
        var rule = json["route"]!["rules"]!.AsArray().Single(r => r!["domain_suffix"] is JsonArray a && a.Any(x => (string)x! == "xn--p1ai"))!;
        Assert.Equal("direct", (string)rule["outbound"]!);
        Assert.Contains(rule["process_path_regex"]!.AsArray(), x => ((string)x!).Contains("HomeApp"));
        var suffixes = rule["domain_suffix"]!.AsArray().Select(x => (string)x!).ToList();
        foreach (var host in new[] { "zakupki.gov.ru", "xn--80az8a.xn--d1aqf.xn--p1ai", "nspd.gov.ru", "pkk.rosreestr.ru", "rusprofile.ru", "egrul.nalog.ru", "focus.kontur.ru", "gisp.gov.ru", "indparks.ru", "russiaindustrialpark.ru", "yandex.ru", "2gis.ru", "avito.ru", "hh.ru", "tver.mk.ru", "ostov-tver.ru", "tverinvest.ru", "admoblkaluga.ru", "kaluga-gov.ru", "oezkaluga.ru", "investkaluga.com", "kaluga24.tv", "tularegion.ru", "stroy.tularegion.ru", "econom.tularegion.ru", "tulacity.gosuslugi.ru", "uzlovaya.ru", "tula.mk.ru", "myslo.ru", "ryazan.gov.ru", "admrzn.ru", "investryazan.ru", "rzn.info", "kaluga.mk.ru", "ryazan.mk.ru", "emmauss.ru", "ryazan-gov.ru" })
            Assert.True(suffixes.Any(s => host == s || host.EndsWith("." + s)), host);
        Assert.DoesNotContain(suffixes, s => "example.guru".EndsWith("." + s) || "github.com".EndsWith("." + s));
        Assert.Contains(json["dns"]!["rules"]!.AsArray(), r => r!["domain_suffix"] is JsonArray a && a.Any(x => (string)x! == "ru") && (string)r["server"]! == "dns-direct");
        Assert.DoesNotContain(Generate(Config("de"), dir)["route"]!["rules"]!.AsArray(), r => r!["domain_suffix"] is JsonArray a && a.Any(x => (string)x! == "ru"));
    }

    [Fact]
    public void A_country_without_domains_uses_ip_only_and_no_country_means_no_rules()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var de = Generate(Config("de"), dir);
        Assert.Equal("country-de-ip", (string)de["route"]!["rule_set"]![0]!["tag"]!);
        Assert.Single(de["route"]!["rule_set"]!.AsArray());
        Assert.DoesNotContain(de["dns"]!["rules"]!.AsArray(), r => r!["rule_set"] is not null);

        var none = Generate(Config(""), dir);
        Assert.Null(none["route"]!["rule_set"]);
    }

    [Fact]
    public void Embedded_sets_match_the_manifest_and_every_option_resolves()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        foreach (var option in CountryRules.Options)
            Assert.NotEmpty(CountryRules.Prepare(option.Code, dir));
        Assert.Equal("", CountryRules.Normalize("zz"));
        Assert.Equal("ru", CountryRules.Normalize(" RU "));
    }
}
