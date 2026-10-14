package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Base64

class RoutingConfigTest {
    private fun JSONObject.routingRules(): org.json.JSONArray {
        val rules = getJSONArray("rules")
        return org.json.JSONArray((0 until rules.length()).map { rules.getJSONObject(it) }
            .filter { it.optString("action") != "sniff" })
    }

    private val node = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443#Test").single()

    @Test fun proxyBootstrapUsesPhysicalDnsAndSitesRetainResolvedDomains() {
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy(sites = listOf("direct.example"))))
        assertEquals("dns-direct", config.getJSONObject("route").getString("default_domain_resolver"))
        assertTrue(config.getJSONObject("dns").getBoolean("reverse_mapping"))
        assertEquals("dns-remote", config.getJSONObject("dns").getString("final"))
        assertEquals("dns-direct", config.getJSONObject("dns").getJSONArray("rules").getJSONObject(0).getString("server"))
        assertEquals("sniff", config.getJSONObject("route").getJSONArray("rules").getJSONObject(0).getString("action"))
    }

    @Test fun onlyListedSitesResolveThroughProxyAndOtherDomainsUsePhysicalDns() {
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy(sites = listOf("proxy.example"), siteMode = SiteMode.ONLY_LIST)))
        val dns = config.getJSONObject("dns")
        assertEquals("dns-direct", dns.getString("final"))
        val rule = dns.getJSONArray("rules").getJSONObject(0)
        assertEquals("dns-remote", rule.getString("server"))
        assertEquals("proxy.example", rule.getJSONArray("domain").getString(0))
        assertEquals(".proxy.example", rule.getJSONArray("domain_suffix").getString(0))
    }

    @Test fun generatedDnsUsesModernSchemaAndDefinesNaiveResolver() {
        val servers = JSONObject(RoutingConfig.build(node, RoutePolicy()))
            .getJSONObject("dns").getJSONArray("servers")
        val byTag = (0 until servers.length()).associateBy({ servers.getJSONObject(it).getString("tag") }, { servers.getJSONObject(it) })
        val remote = byTag.getValue("dns-remote")
        assertEquals("https", remote.getString("type"))
        assertEquals("1.1.1.1", remote.getString("server"))
        assertEquals("/dns-query", remote.getString("path"))
        assertEquals("proxy", remote.getString("detour"))
        assertFalse(remote.has("address"))
        assertEquals("local", byTag.getValue("dns-direct").getString("type"))

        val naive = NodeShareParser.parse("naive://user:pass@edge.example:443").single()
        val resolverTag = JSONObject(RoutingConfig.build(naive, RoutePolicy()))
            .getJSONArray("outbounds").getJSONObject(0).getJSONObject("domain_resolver").getString("server")
        assertTrue(byTag.containsKey(resolverTag))
    }

    @Test fun routesSelectedAppsAndListedSites() {
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy(listOf("com.example.mail"), listOf("https://Mail.Example/a"), SiteMode.ONLY_LIST)))
        val rules = config.getJSONObject("route").routingRules()
        assertEquals("com.example.mail", config.getJSONArray("inbounds").getJSONObject(0).getJSONArray("include_package").getString(0))
        assertEquals("gvisor", config.getJSONArray("inbounds").getJSONObject(0).getString("stack"))
        assertEquals("mail.example", rules.getJSONObject(0).getJSONArray("domain").getString(0))
        assertEquals("proxy", rules.getJSONObject(0).getString("outbound"))
        for (index in 0 until rules.length()) assertTrue(!rules.getJSONObject(index).has("package_name"))
    }

    @Test fun appAndSiteAssignmentsUseDifferentNodeOutboundsAndAutoRotation() {
        val second = NodeShareParser.parse("vless://22222222-2222-2222-2222-222222222222@backup.example:443#Backup").single()
        val policy = RoutePolicy(
            appNodeRoutes = mapOf("com.example.chat" to node.routeId),
            siteNodeRoutes = mapOf("chat.example" to second.routeId),
            autoRotationEnabled = true,
            autoRotationIntervalSeconds = 120,
        )
        val config = JSONObject(RoutingConfig.build(listOf(node, second), 0, policy))
        val outbounds = config.getJSONArray("outbounds")
        val urltest = (0 until outbounds.length()).map { outbounds.getJSONObject(it) }.single { it.optString("tag") == OrderedFailoverConfig.GLOBAL_TAG }
        assertEquals("selector", urltest.getString("type"))
        assertEquals("block", urltest.getString("default"))
        assertEquals(3, urltest.getJSONArray("outbounds").length())
        val rules = config.getJSONObject("route").routingRules()
        assertEquals("proxy-1", rules.getJSONObject(0).getString("outbound"))
        assertEquals("chat.example", rules.getJSONObject(0).getJSONArray("domain").getString(0))
        assertEquals("proxy-0", rules.getJSONObject(1).getString("outbound"))
        assertEquals("com.example.chat", rules.getJSONObject(1).getJSONArray("package_name").getString(0))
        assertEquals(OrderedFailoverConfig.GLOBAL_TAG, config.getJSONObject("route").getString("final"))
    }

    @Test fun autoSelectionSkipsExcludedNodesAndUsesAllWhenEverythingIsExcluded() {
        val second = NodeShareParser.parse("vless://22222222-2222-2222-2222-222222222222@backup.example:443#Backup").single()
        fun autoTags(excluded: Set<String>): List<String> {
            val config = JSONObject(RoutingConfig.build(listOf(node, second), 0, RoutePolicy(autoRotationEnabled = true, autoExcludedRouteIds = excluded)))
            val outbounds = config.getJSONArray("outbounds")
            val group = (0 until outbounds.length()).map { outbounds.getJSONObject(it) }.single { it.optString("tag") == OrderedFailoverConfig.GLOBAL_TAG }
            val tags = group.getJSONArray("outbounds")
            return (0 until tags.length()).map { tags.getString(it) }.filter { it != "block" }
        }
        assertEquals(listOf("proxy-0"), autoTags(setOf(second.routeId)))
        assertEquals(listOf("proxy-0", "proxy-1"), autoTags(emptySet()))
        assertEquals(listOf("proxy-0", "proxy-1"), autoTags(setOf(node.routeId, second.routeId)))
    }

    @Test fun excludedCountryNodesAreRemovedFromConfigAndKeepOtherNodes() {
        fun named(label: String, id: String) = NodeShareParser.parse(
            "vless://$id@$id.example:443#" + java.net.URLEncoder.encode(label, "UTF-8").replace("+", "%20")
        ).single()
        val ru = named("🇷🇺 Россия-1", "33333333-3333-3333-3333-333333333333")
        val de = named("🇩🇪 Германия-4", "44444444-4444-4444-4444-444444444444")
        val unknown = named("↔️ Автовыбор", "55555555-5555-5555-5555-555555555555")
        val config = JSONObject(RoutingConfig.build(listOf(ru, de, unknown), 0, RoutePolicy(autoRotationEnabled = true, excludedCountries = setOf("RU"))))
        val outbounds = config.getJSONArray("outbounds")
        val group = (0 until outbounds.length()).map { outbounds.getJSONObject(it) }.single { it.optString("tag") == OrderedFailoverConfig.GLOBAL_TAG }
        val tags = group.getJSONArray("outbounds")
        assertEquals(listOf("proxy-0", "proxy-1"), (0 until tags.length()).map { tags.getString(it) }.filter { it != "block" })
        assertFalse(config.toString().contains("33333333-3333"))
    }

    @Test fun appSpecificRoutePackageIsIncludedInSelectedAppsTunnel() {
        val second = NodeShareParser.parse("vless://22222222-2222-2222-2222-222222222222@backup.example:443#Backup").single()
        val config = JSONObject(RoutingConfig.build(listOf(node, second), 0, RoutePolicy(appNodeRoutes = mapOf("com.example.chat" to second.routeId))))
        val include = config.getJSONArray("inbounds").getJSONObject(0).getJSONArray("include_package")
        assertEquals("com.example.chat", include.getString(0))
    }

    @Test fun exceptionModeSendsListedSitesDirect() {
        val rules = JSONObject(RoutingConfig.build(node, RoutePolicy(sites = listOf("docs.example")))).getJSONObject("route").routingRules()
        assertEquals("direct", rules.getJSONObject(0).getString("outbound"))
        assertEquals("docs.example", rules.getJSONObject(0).getJSONArray("domain").getString(0))
        assertEquals(".docs.example", rules.getJSONObject(0).getJSONArray("domain_suffix").getString(0))
    }

    @Test fun onlySitesSendsEverythingElseDirectEvenWhenListIsEmpty() {
        val route = JSONObject(RoutingConfig.build(node, RoutePolicy(siteMode = SiteMode.ONLY_LIST))).getJSONObject("route")
        assertEquals("direct", route.getString("final"))
        assertEquals(0, route.routingRules().length())
    }

    @Test fun onlySitesLeavesNeighborDomainsDirect() {
        val route = JSONObject(RoutingConfig.build(node, RoutePolicy(sites = listOf("video.example"), siteMode = SiteMode.ONLY_LIST))).getJSONObject("route")
        assertEquals("direct", route.getString("final"))
        assertEquals("proxy", route.routingRules().getJSONObject(0).getString("outbound"))
    }

    @Test fun onlySelectedApplicationsEnterTheTunnel() {
        val inbound = JSONObject(RoutingConfig.build(node, RoutePolicy(apps = listOf("com.example.mail")))).getJSONArray("inbounds").getJSONObject(0)
        val route = JSONObject(RoutingConfig.build(node, RoutePolicy(apps = listOf("com.example.mail")))).getJSONObject("route")
        assertEquals("com.example.mail", inbound.getJSONArray("include_package").getString(0))
        assertEquals("proxy", route.getString("final"))
    }

    @Test fun allApplicationsEnterTunnelExceptSelectedPackages() {
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy(apps = listOf("com.example.mail"), excludeSelectedApps = true)))
        val inbound = config.getJSONArray("inbounds").getJSONObject(0)
        assertEquals("com.example.mail", inbound.getJSONArray("exclude_package").getString(0))
        assertFalse(inbound.has("include_package"))
        assertEquals("proxy", config.getJSONObject("route").getString("final"))
    }

    @Test fun onlySelectedSitesWithinSelectedAppsUseProxy() {
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy(apps = listOf("com.example.mail"), sites = listOf("mail.example"), siteMode = SiteMode.ONLY_LIST)))
        val route = config.getJSONObject("route")
        val rules = route.routingRules()
        assertEquals("com.example.mail", config.getJSONArray("inbounds").getJSONObject(0).getJSONArray("include_package").getString(0))
        assertEquals("proxy", rules.getJSONObject(0).getString("outbound"))
        assertTrue(!rules.getJSONObject(0).has("package_name"))
        assertEquals("direct", route.getString("final"))
        assertEquals(1, rules.length())
    }

    @Test fun importedOnlyListDropsOldProxyRulesAndKeepsNeighbourDirect() {
        val imported = """{"inbounds":[{"type":"tun"}],"outbounds":[{"type":"vless","tag":"proxy"},{"type":"direct","tag":"direct"},{"type":"block","tag":"block"}],"route":{"final":"proxy","rules":[{"domain":["neighbour.example"],"outbound":"proxy"},{"outbound":"proxy"},{"package_name":["com.example.mail"],"outbound":"direct"},{"domain":["safe.example"],"outbound":"direct"}]}}"""
        val result = JSONObject(RoutingConfig.applyToImported(imported, RoutePolicy(sites = listOf("allowed.example"), siteMode = SiteMode.ONLY_LIST)))
        val route = result.getJSONObject("route")
        assertEquals("direct", route.getString("final"))
        val rules = route.routingRules()
        assertEquals(2, rules.length())
        assertEquals("allowed.example", rules.getJSONObject(0).getJSONArray("domain").getString(0))
        assertEquals("proxy", rules.getJSONObject(0).getString("outbound"))
        assertEquals("direct", rules.getJSONObject(1).getString("outbound"))
        for (index in 0 until rules.length()) assertTrue(!rules.getJSONObject(index).has("package_name"))
    }

    @Test fun importedConfigScopesTunToExplicitlySelectedApps() {
        val imported = """{"inbounds":[{"type":"tun"}],"outbounds":[{"type":"vless","tag":"proxy"},{"type":"direct","tag":"direct"}],"route":{"final":"proxy"}}"""

        val result = JSONObject(RoutingConfig.applyToImported(imported, RoutePolicy(apps = listOf("com.example.selected"))))
        val tun = result.getJSONArray("inbounds").getJSONObject(0)
        val allowed = tun.getJSONArray("include_package")

        assertEquals("gvisor", tun.getString("stack"))
        assertEquals(1, allowed.length())
        assertEquals("com.example.selected", allowed.getString(0))
        assertEquals("proxy", result.getJSONObject("route").getString("final"))
    }

    @Test fun importedConfigExcludesSelectedAppsFromAllAppTunnel() {
        val imported = """{"inbounds":[{"type":"tun","include_package":["old.package"]} ],"outbounds":[{"type":"vless","tag":"proxy"}],"route":{"final":"proxy"}}"""
        val result = JSONObject(RoutingConfig.applyToImported(imported, RoutePolicy(apps = listOf("com.example.direct"), excludeSelectedApps = true)))
        val tun = result.getJSONArray("inbounds").getJSONObject(0)
        assertEquals("com.example.direct", tun.getJSONArray("exclude_package").getString(0))
        assertFalse(tun.has("include_package"))
        assertEquals("proxy", result.getJSONObject("route").getString("final"))
    }

    @Test fun emptyExclusionListMeansAllAppsUseTunnel() {
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy(excludeSelectedApps = true)))
        val inbound = config.getJSONArray("inbounds").getJSONObject(0)
        assertFalse(inbound.has("include_package"))
        assertFalse(inbound.has("exclude_package"))
        assertEquals("proxy", config.getJSONObject("route").getString("final"))
    }

    @Test fun importedSingBoxTunnelKeepsItsConfigAndReceivesRules() {
        val imported = """{"inbounds":[{"type":"tun","tag":"tun-in","stack":"mixed"}],"outbounds":[{"type":"vless","tag":"proxy","server":"a.example","server_port":443,"uuid":"id"},{"type":"direct","tag":"direct"}],"route":{"final":"proxy"}}"""
        val result = JSONObject(RoutingConfig.applyToImported(imported, RoutePolicy(sites = listOf("https://docs.example/x"), siteMode = SiteMode.ONLY_LIST)))
        assertEquals("docs.example", result.getJSONObject("route").routingRules().getJSONObject(0).getJSONArray("domain").getString(0))
        assertEquals("direct", result.getJSONObject("route").getString("final"))
        assertEquals("tun-in", result.getJSONArray("inbounds").getJSONObject(0).getString("tag"))
        assertEquals("gvisor", result.getJSONArray("inbounds").getJSONObject(0).getString("stack"))
    }

    @Test fun vlessRealityWebSocketMapsTlsAndTransportParameters() {
        val outbound = outbound("vless://11111111-1111-1111-1111-111111111111@edge.example:443?encryption=none&security=reality&sni=front.example&fp=chrome&pbk=public-key&sid=abcd&type=ws&host=cdn.example&path=%2Fsocket&ed=256&eh=Sec-WebSocket-Protocol&flow=xtls-rprx-vision")
        assertEquals("11111111-1111-1111-1111-111111111111", outbound.getString("uuid"))
        assertEquals("xtls-rprx-vision", outbound.getString("flow"))
        val tls = outbound.getJSONObject("tls")
        assertEquals("front.example", tls.getString("server_name"))
        assertEquals("chrome", tls.getJSONObject("utls").getString("fingerprint"))
        assertEquals("public-key", tls.getJSONObject("reality").getString("public_key"))
        assertEquals("abcd", tls.getJSONObject("reality").getString("short_id"))
        val transport = outbound.getJSONObject("transport")
        assertEquals("ws", transport.getString("type"))
        assertEquals("/socket", transport.getString("path"))
        assertEquals("cdn.example", transport.getJSONObject("headers").getString("Host"))
        assertEquals(256, transport.getInt("max_early_data"))
        assertEquals("Sec-WebSocket-Protocol", transport.getString("early_data_header_name"))
    }

    @Test fun trojanGrpcMapsTlsAndServiceName() {
        val outbound = outbound("trojan://p%40ss@edge.example:443?sni=tls.example&allowInsecure=1&alpn=h2%2Chttp%2F1.1&type=grpc&serviceName=Tunnel%2FOne")
        assertEquals("p@ss", outbound.getString("password"))
        val tls = outbound.getJSONObject("tls")
        assertEquals("tls.example", tls.getString("server_name"))
        assertTrue(tls.getBoolean("insecure"))
        assertEquals("http/1.1", tls.getJSONArray("alpn").getString(1))
        assertEquals("grpc", outbound.getJSONObject("transport").getString("type"))
        assertEquals("Tunnel/One", outbound.getJSONObject("transport").getString("service_name"))
    }

    @Test fun tuicAndHysteria2MapCredentialsAndTlsOptions() {
        val tuic = outbound("tuic://11111111-1111-1111-1111-111111111111:secret@tuic.example:443?congestion_control=bbr&sni=tuic-sni.example&alpn=h3")
        assertEquals("11111111-1111-1111-1111-111111111111", tuic.getString("uuid"))
        assertEquals("secret", tuic.getString("password"))
        assertEquals("bbr", tuic.getString("congestion_control"))
        assertEquals("tuic-sni.example", tuic.getJSONObject("tls").getString("server_name"))
        assertEquals("h3", tuic.getJSONObject("tls").getJSONArray("alpn").getString(0))

        val hy2 = outbound("hysteria2://hy2-secret@hy2.example:443?sni=hy2-sni.example&insecure=true")
        assertEquals("hy2-secret", hy2.getString("password"))
        assertEquals("hy2-sni.example", hy2.getJSONObject("tls").getString("server_name"))
        assertTrue(hy2.getJSONObject("tls").getBoolean("insecure"))
    }

    @Test fun socksAndHttpMapUserInfoCredentials() {
        val socks = outbound("socks5://alice:p%40ss@proxy.example:1080")
        assertEquals("alice", socks.getString("username"))
        assertEquals("p@ss", socks.getString("password"))
        assertEquals("5", socks.getString("version"))
        val http = outbound("http://bob:secret@proxy.example:8080")
        assertEquals("bob", http.getString("username"))
        assertEquals("secret", http.getString("password"))
    }

    @Test fun shadowsocksOutboundDecodesBase64CredentialsWithApi24Helper() {
        val shadowsocks = outbound("ss://YWVzLTEyOC1nY206c3ludGhldGljLXBhc3N3b3Jk@ss.example:8388")
        assertEquals("aes-128-gcm", shadowsocks.getString("method"))
        assertEquals("synthetic-password", shadowsocks.getString("password"))
    }

    @Test fun vmessMapsCipherTlsAndWebSocketFields() {
        val vmess = """{"v":"2","ps":"ws","add":"vmess.example","port":"443","id":"11111111-1111-1111-1111-111111111111","aid":"0","scy":"chacha20-poly1305","net":"ws","tls":"tls","sni":"sni.example","alpn":"h2,http/1.1","fp":"firefox","host":"cdn.example","path":"/vmess"}"""
        val link = "vmess://${Base64.getEncoder().encodeToString(vmess.toByteArray())}"
        val outbound = outbound(link)
        assertEquals("chacha20-poly1305", outbound.getString("security"))
        assertEquals("11111111-1111-1111-1111-111111111111", outbound.getString("uuid"))
        assertEquals("sni.example", outbound.getJSONObject("tls").getString("server_name"))
        assertEquals("firefox", outbound.getJSONObject("tls").getJSONObject("utls").getString("fingerprint"))
        val transport = outbound.getJSONObject("transport")
        assertEquals("ws", transport.getString("type"))
        assertEquals("/vmess", transport.getString("path"))
        assertEquals("cdn.example", transport.getJSONObject("headers").getString("Host"))
    }

    @Test fun importedPolicyUsesConfiguredOutboundTagAndDefaultPolicyPreservesConfig() {
        val imported = """{"inbounds":[{"type":"tun"}],"outbounds":[{"type":"vless","tag":"edge-out"},{"type":"direct","tag":"direct"}],"route":{"final":"edge-out","rules":[{"domain":["old.example"],"outbound":"edge-out"}]}}"""
        assertEquals(imported, RoutingConfig.applyToImported(imported, RoutePolicy()))
        val result = JSONObject(RoutingConfig.applyToImported(imported, RoutePolicy(sites = listOf("allowed.example"), siteMode = SiteMode.ONLY_LIST)))
        val route = result.getJSONObject("route")
        assertEquals("direct", route.getString("final"))
        assertEquals("edge-out", route.routingRules().getJSONObject(0).getString("outbound"))
        assertEquals(".allowed.example", route.routingRules().getJSONObject(0).getJSONArray("domain_suffix").getString(0))
    }

    @Test fun importedConfigStillAppliesAutoSelectionWithoutOtherFilters() {
        val imported = """{"inbounds":[{"type":"tun"}],"outbounds":[{"type":"vless","tag":"edge-a","server":"a.example","server_port":443,"uuid":"11111111-1111-1111-1111-111111111111"},{"type":"vless","tag":"edge-b","server":"b.example","server_port":443,"uuid":"22222222-2222-2222-2222-222222222222"},{"type":"direct","tag":"direct"}],"route":{"final":"edge-a"}}"""
        val updated = JSONObject(RoutingConfig.applyToImported(imported, RoutePolicy(autoRotationEnabled = true)))
        val outbounds = updated.getJSONArray("outbounds")
        assertTrue((0 until outbounds.length()).map { outbounds.getJSONObject(it) }.any { it.optString("tag") == OrderedFailoverConfig.GLOBAL_TAG && it.optString("type") == "selector" })
        assertEquals(OrderedFailoverConfig.GLOBAL_TAG, updated.getJSONObject("route").getString("final"))
    }

    @Test fun importedPolicyFailsClearlyWithoutProxyOutbound() {
        val imported = """{"inbounds":[{"type":"tun"}],"outbounds":[{"type":"direct","tag":"direct"},{"type":"block","tag":"block"}],"route":{"final":"direct"}}"""
        assertThrows(IllegalArgumentException::class.java) {
            RoutingConfig.applyToImported(imported, RoutePolicy(sites = listOf("allowed.example")))
        }
    }

    @Test fun unsupportedV2rayTransportFailsClearly() {
        assertThrows(IllegalArgumentException::class.java) {
            RoutingConfig.build(NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443?type=unknownnet").single(), RoutePolicy())
        }
    }

    @Test fun nodeWithUnsupportedTransportIsSkippedInsteadOfBreakingWholeConfig() {
        val unsupported = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@xhttp.example:443?type=xhttp&security=none#Xhttp").single()
        val working = NodeShareParser.parse("vless://22222222-2222-2222-2222-222222222222@ws.example:443?type=ws&path=%2Fws&security=none#Ws").single()
        val config = JSONObject(RoutingConfig.build(listOf(unsupported, working), 0, RoutePolicy()))
        val outbounds = config.getJSONArray("outbounds")
        val vless = (0 until outbounds.length()).map { outbounds.getJSONObject(it) }.filter { it.optString("type") == "vless" }
        assertEquals(listOf("ws.example"), vless.map { it.getString("server") })
    }

    private fun outbound(uri: String): JSONObject = JSONObject(RoutingConfig.build(NodeShareParser.parse(uri).single(), RoutePolicy()))
        .getJSONArray("outbounds").getJSONObject(0)
}
