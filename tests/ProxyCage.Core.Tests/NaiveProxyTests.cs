using System.Text.Json.Nodes;

using ProxyCage.Core;



namespace ProxyCage.Core.Tests;



public class NaiveProxyTests

{

    [Fact]

    public void Parses_naive_uri()

    {

        var uri = "naive://bsv:secret@site.roomspace.team:8443?sni=site.roomspace.team#BSV";

        Assert.True(NaiveProxyHelper.TryParseUri(uri, out var s));

        Assert.NotNull(s);

        Assert.Equal("site.roomspace.team", s!.Server);

        Assert.Equal(8443, s.Port);

        Assert.Equal("bsv", s.Username);

        Assert.Equal("secret", s.Password);

        Assert.Equal("site.roomspace.team", s.ServerName);

    }



    [Fact]

    public void BuildUri_roundtrips()

    {

        var settings = new NaiveProxySettings

        {

            Enabled = true,

            Server = "site.roomspace.team",

            Port = 8443,

            Username = "bsv",

            Password = "secret",

            ServerName = "site.roomspace.team",

            Remark = "BSV",

        };

        var uri = NaiveProxyHelper.BuildUri(settings);

        Assert.True(NaiveProxyHelper.TryParseUri(uri, out var parsed));

        Assert.Equal(settings.Server, parsed!.Server);

        Assert.Equal(settings.Username, parsed.Username);

        Assert.Equal(settings.Password, parsed.Password);

        Assert.Equal(settings.ServerName, parsed.ServerName);

    }



    [Fact]

    public void Subscription_parser_reads_naive_line()

    {

        var body = "naive://user:pass@185.76.13.167:8443?sni=site.roomspace.team#Test\n";

        var nodes = SubscriptionParser.Parse(body);

        Assert.Single(nodes);

        Assert.Equal(ProxyProtocol.Naive, nodes[0].Protocol);

        Assert.Equal("185.76.13.167", nodes[0].Server);

    }



    [Fact]

    public void Outbound_builder_emits_naive_with_tls()

    {

        var node = NaiveProxyHelper.ToNode(new NaiveProxySettings

        {

            Enabled = true,

            Server = "site.roomspace.team",

            Port = 8443,

            Username = "bsv",

            Password = "test",

            ServerName = "site.roomspace.team",

        });

        node.Tag = "n001";



        var json = OutboundBuilder.Build(node);

        Assert.Equal("naive", json["type"]!.GetValue<string>());

        Assert.Equal("bsv", json["username"]!.GetValue<string>());

        Assert.Equal("test", json["password"]!.GetValue<string>());

        Assert.Equal("site.roomspace.team", json["tls"]!["server_name"]!.GetValue<string>());

    }



    [Fact]

    public void Naive_never_dials_ipv6()

    {

        var node = NaiveProxyHelper.ToNode(new NaiveProxySettings

        {

            Enabled = true,

            Server = "site.roomspace.team",

            Port = 8443,

            Username = "bsv",

            Password = "test",

            ServerName = "site.roomspace.team",

        });



        var json = OutboundBuilder.Build(node);

        Assert.Equal("ipv4_only", json["domain_strategy"]!.GetValue<string>());

    }



    [Fact]

    public void GenerateForConfig_includes_naive_node_without_extra_inbound()

    {

        var cfg = new CehoConfig

        {

            Apps =

            {

                new AppEntry { Name = "test", Folder = @"C:\Test\App", Enabled = true },

            },

        };

        var nodes = new List<ProxyNode>

        {

            NaiveProxyHelper.ToNode(new NaiveProxySettings

            {

                Enabled = true,

                Server = "site.roomspace.team",

                Port = 8443,

                Username = "bsv",

                Password = "test",

                ServerName = "site.roomspace.team",

            }),

        };

        nodes[0].Tag = "n001";



        var raw = SingBoxConfigGenerator.GenerateForConfig(nodes, cfg);

        Assert.Contains("\"type\": \"naive\"", raw);

        Assert.Contains("site.roomspace.team", raw);

        Assert.DoesNotContain("naive-mixed-in", raw);

        Assert.DoesNotContain("\"listen_port\": 1080", raw);

    }



    [Fact]

    public void Legacy_naive_block_migrates_to_subscription()

    {

        var path = Path.Combine(Path.GetTempPath(), $"ceho-naive-{Guid.NewGuid():N}.json");

        try

        {

            var cfg = new CehoConfig

            {

                NaiveProxy =

                {

                    Enabled = true,

                    Server = "185.76.13.167",

                    Port = 8443,

                    Username = "bsv",

                    Password = "secret-token",

                    ServerName = "site.roomspace.team",

                    Remark = "BSV lab",

                },

            };

            cfg.Save(path);

            var loaded = CehoConfig.Load(path);

            Assert.False(loaded.NaiveProxy.IsConfigured);

            Assert.Single(loaded.Subscriptions);

            Assert.StartsWith("naive://", loaded.Subscriptions[0].Url, StringComparison.OrdinalIgnoreCase);

            Assert.Equal("BSV lab", loaded.Subscriptions[0].Name);

            Assert.True(NaiveProxyHelper.TryParseUri(loaded.Subscriptions[0].Url, out var s));

            Assert.Equal("secret-token", s!.Password);

        }

        finally

        {

            if (File.Exists(path)) File.Delete(path);

        }

    }



    [Fact]

    public void Naive_only_pool_works_when_unknown_country_is_excluded()

    {

        var nodes = SubscriptionParser.Parse(

            "naive://bsv:secret@site.roomspace.team:8443?sni=site.roomspace.team#Caddy prod sim\n");

        var cfg = new CehoConfig

        {

            Apps = { new AppEntry { Name = "Opera", Folder = @"C:\Apps\Opera", Enabled = true } },

            ExcludedCountries = { "NL", CountryResolver.Unknown, "RU" },

            PreferredCountries = { "DE", "US" },

        };



        var pool = SingBoxConfigGenerator.BuildPool(nodes, cfg);

        Assert.Single(pool);

        Assert.Equal(ProxyProtocol.Naive, pool[0].Protocol);



        var json = SingBoxConfigGenerator.GenerateForConfig(nodes, cfg);

        Assert.Contains("\"type\": \"naive\"", json);

        Assert.Contains("site.roomspace.team", json);

    }



    [Fact]

    public void Naive_ip_upstream_keeps_sni_separate_from_server()

    {

        var s = new NaiveProxySettings

        {

            Enabled = true,

            Server = "185.76.13.167",

            Port = 8443,

            Username = "bsv",

            Password = "x",

            ServerName = "site.roomspace.team",

        };

        var node = NaiveProxyHelper.ToNode(s);

        var json = OutboundBuilder.Build(node);

        Assert.Equal("185.76.13.167", json["server"]!.GetValue<string>());

        Assert.Equal("site.roomspace.team", json["tls"]!["server_name"]!.GetValue<string>());

        Assert.Equal("bsv", json["username"]!.GetValue<string>());

    }

}


