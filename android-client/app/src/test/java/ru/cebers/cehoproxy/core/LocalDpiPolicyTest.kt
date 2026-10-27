package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class LocalDpiPolicyTest {
    private val app = "com.google.android.youtube"
    private val other = "com.example.other"
    private val node = NodeShareParser.parse("vless://synthetic-id@edge.example:443#test").single()
    private val sets = listOf(CountryRuleSet("country-ru-ip", "/synthetic/ru.srs", false), CountryRuleSet("country-ru-domains", "/synthetic/domains.srs", true))

    @Test fun modeCannotExpandSelectedVpnApplications() {
        assertEquals(listOf(app), LocalDpiPolicy.activeApps(listOf(app, other), listOf(app), false))
        assertTrue(LocalDpiPolicy.activeApps(listOf(app), emptyList(), false).isEmpty())
        assertEquals(listOf(other), LocalDpiPolicy.activeApps(listOf(app, other), listOf(app), true))
        assertEquals(listOf(app), LocalDpiPolicy.normalize(listOf(" $app ", app, "", "../bad", "ru.cebers.cehoproxy")))
    }

    @Test fun importedConfigDisablesDpiWithoutDiscardingPreference() {
        val profile = ClientProfile(apps = listOf(app), localDpiApps = listOf(app), importedConfig = "{}")
        assertTrue(LocalDpiPolicy.activeApps(profile).isEmpty())
        assertEquals(listOf(app), ProfileCodec.decode(ProfileCodec.encode(profile)).localDpiApps)
    }

    @Test fun oldProfilesDefaultToNoDpiAndBackupsRespectScope() {
        assertTrue(ProfileCodec.decode("{}").localDpiApps.isEmpty())
        val source = ClientProfile(nodes = listOf(node), apps = listOf(app), localDpiApps = listOf(app))
        val destination = ClientProfile(localDpiApps = listOf(other))
        assertEquals(listOf(app), ProfileCodec.decode(ProfileCodec.encode(source)).localDpiApps)
        for (scope in listOf(ProfileExportScope.SETTINGS, ProfileExportScope.ALL)) {
            assertEquals(listOf(app), ProfileBackup.restore(destination, ProfileBackup.encode(source, scope))!!.localDpiApps)
        }
        assertEquals(listOf(other), ProfileBackup.restore(destination, ProfileBackup.encode(source, ProfileExportScope.SUBSCRIPTIONS))!!.localDpiApps)
    }

    @Test fun manualSitesOutrankDpiAndDpiOutranksCountryAndAssignedApp() {
        for (mode in SiteMode.entries) {
            val config = JSONObject(RoutingConfig.build(node, RoutePolicy(apps = listOf(app, other), localDpiApps = listOf(app),
                sites = listOf("manual.example"), siteNodeRoutes = mapOf("assigned.example" to node.routeId),
                appNodeRoutes = mapOf(other to node.routeId), siteMode = mode, countryRuleSets = sets)))
            val rules = config.routeRulesWithoutDefaults()
            assertEquals("sniff", rules.getJSONObject(0).getString("action"))
            assertEquals("assigned.example", rules.getJSONObject(1).getJSONArray("domain").getString(0))
            assertEquals("manual.example", rules.getJSONObject(2).getJSONArray("domain").getString(0))
            val reject = rules.getJSONObject(3)
            assertEquals(app, reject.getJSONArray("package_name").getString(0))
            assertEquals(1, reject.getJSONArray("package_name").length())
            assertEquals("udp", reject.getString("network"))
            assertEquals(443, reject.getJSONArray("port").getInt(0))
            assertEquals("reject", reject.getString("action"))
            assertTrue(reject.getBoolean("no_drop"))
            val fragment = rules.getJSONObject(4)
            assertEquals("tcp", fragment.getString("network"))
            assertEquals("direct", fragment.getString("outbound"))
            assertTrue(fragment.getBoolean("tls_record_fragment"))
            assertFalse(fragment.has("tls_spoof"))
            assertFalse(fragment.has("tls_fragment"))
            assertEquals("direct", rules.getJSONObject(5).getString("outbound"))
            assertEquals("country-ru-ip", rules.getJSONObject(6).getJSONArray("rule_set").getString(0))
            assertEquals(other, rules.getJSONObject(7).getJSONArray("package_name").getString(0))
            assertEquals("proxy", rules.getJSONObject(7).getString("outbound"))
            assertEquals(if (mode == SiteMode.ONLY_LIST) "direct" else "proxy", config.getJSONObject("route").getString("final"))
            val dns = config.getJSONObject("dns").getJSONArray("rules")
            assertEquals("assigned.example", dns.getJSONObject(0).getJSONArray("domain").getString(0))
            assertEquals("manual.example", dns.getJSONObject(1).getJSONArray("domain").getString(0))
            assertEquals(app, dns.getJSONObject(2).getJSONArray("package_name").getString(0))
            assertEquals("dns-direct", dns.getJSONObject(2).getString("server"))
            assertTrue(dns.getJSONObject(3).has("rule_set"))
            assertEquals(2, config.getJSONArray("inbounds").getJSONObject(0).getJSONArray("include_package").length())
        }
    }

    @Test fun standaloneLocalDpiNeedsNoSubscriptionButStillNeedsSelectedApps() {
        val config = JSONObject(RoutingConfig.build(emptyList(), 0, RoutePolicy(apps = listOf(app, other), localDpiApps = listOf(app))))
        assertEquals("block", config.getJSONObject("route").getString("final"))
        assertEquals("block", config.getJSONObject("dns").getJSONArray("servers").getJSONObject(0).getString("detour"))
        assertEquals("reject", config.getJSONObject("dns").getJSONArray("rules").getJSONObject(1).getString("action"))
        val rules = config.routeRulesWithoutDefaults()
        for (index in 1 until rules.length()) assertEquals(listOf(app), rules.getJSONObject(index).getJSONArray("package_name").let { array -> (0 until array.length()).map(array::getString) })
        assertEquals(app, config.getJSONArray("inbounds").getJSONObject(0).getJSONArray("include_package").getString(0))
        assertEquals(listOf("direct", "block"), config.getJSONArray("outbounds").let { array -> (0 until array.length()).map { array.getJSONObject(it).getString("type") } })
        assertThrows(IllegalArgumentException::class.java) { RoutingConfig.build(emptyList(), 0, RoutePolicy(localDpiApps = listOf(app))) }
    }

    @Test fun noDpiOrOutsideScopeProducesNoFragmentOrQuicRestriction() {
        val config = RoutingConfig.build(node, RoutePolicy(apps = listOf(other), localDpiApps = listOf(app)))
        assertFalse(config.contains("tls_record_fragment"))
        assertFalse(config.contains("\"protocol\": \"quic\""))
        assertFalse(config.contains(app))
    }
}
