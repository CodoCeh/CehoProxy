package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.io.File
import java.security.MessageDigest

class CountryRoutingTest {
    private val node = NodeShareParser.parse("vless://synthetic-id@edge.example:443#test").single()
    private val sets = listOf(CountryRuleSet("country-ru-ip", "/private/geoip-ru.srs", false), CountryRuleSet("country-ru-domains", "/private/geosite-category-ru.srs", true))

    @Test fun countryDefaultsOffAndProfileMigrationPreservesManualRulesAndAppScope() {
        val old = """{"apps":["com.example.app"],"sites":["manual.example"],"onlySites":true,"excludeSelectedApps":false}"""
        val profile = ProfileCodec.decode(old)
        assertEquals("", profile.directCountryCode)
        val next = profile.copy(directCountryCode = "ru")
        assertEquals(next, ProfileCodec.decode(ProfileCodec.encode(next)))
        assertEquals(listOf("manual.example"), next.sites)
        assertEquals(listOf("com.example.app"), next.apps)
        assertFalse(next.excludeSelectedApps)
        assertEquals("", ProfileCodec.decode("""{"directCountryCode":"zz"}""").directCountryCode)
    }

    @Test fun backupScopesCarryCountryOnlyForSettingsAndFullBackup() {
        val source = ClientProfile(nodes = listOf(node), directCountryCode = "ru")
        val destination = ClientProfile(directCountryCode = "de", sites = listOf("manual.example"))
        assertEquals("ru", ProfileBackup.decode(ProfileBackup.encode(source))!!.directCountryCode)
        assertEquals("ru", ProfileBackup.restore(destination, ProfileBackup.encode(source, ProfileExportScope.SETTINGS))!!.directCountryCode)
        assertEquals("", ProfileBackup.decode(ProfileBackup.encode(source, ProfileExportScope.SUBSCRIPTIONS))!!.directCountryCode)
        val restored = ProfileBackup.restore(destination, ProfileBackup.encode(source, ProfileExportScope.SUBSCRIPTIONS))!!
        assertEquals("de", restored.directCountryCode)
        assertEquals(destination.sites, restored.sites)
    }

    @Test fun explicitSitesOutrankCountryAndCountryOutranksApplicationAssignment() {
        for (mode in SiteMode.entries) {
            val policy = RoutePolicy(apps = listOf("com.example.app"), sites = listOf("manual.example"), siteMode = mode,
                appNodeRoutes = mapOf("com.example.app" to node.routeId), siteNodeRoutes = mapOf("assigned.example" to node.routeId), countryRuleSets = sets)
            val config = JSONObject(RoutingConfig.build(node, policy))
            val rules = config.getJSONObject("route").getJSONArray("rules")
            assertEquals("sniff", rules.getJSONObject(0).getString("action"))
            assertEquals("assigned.example", rules.getJSONObject(1).getJSONArray("domain").getString(0))
            assertEquals("manual.example", rules.getJSONObject(2).getJSONArray("domain").getString(0))
            assertEquals(if (mode == SiteMode.ONLY_LIST) "proxy" else "direct", rules.getJSONObject(2).getString("outbound"))
            assertEquals("direct", rules.getJSONObject(3).getString("outbound"))
            assertEquals("country-ru-ip", rules.getJSONObject(3).getJSONArray("rule_set").getString(0))
            assertEquals("com.example.app", rules.getJSONObject(4).getJSONArray("package_name").getString(0))
            assertEquals(if (mode == SiteMode.ONLY_LIST) "direct" else "proxy", config.getJSONObject("route").getString("final"))
            val dns = config.getJSONObject("dns").getJSONArray("rules")
            assertEquals("assigned.example", dns.getJSONObject(0).getJSONArray("domain").getString(0))
            assertEquals("manual.example", dns.getJSONObject(1).getJSONArray("domain").getString(0))
            assertEquals("country-ru-domains", dns.getJSONObject(2).getJSONArray("rule_set").getString(0))
            assertEquals("dns-direct", dns.getJSONObject(2).getString("server"))
        }
    }

    @Test fun explicitlyAssignedRussianSiteUsesItsOwnNodeForDnsBeforeCountryDirect() {
        val second = NodeShareParser.parse("vless://other-id@backup.example:443#backup").single()
        val policy = RoutePolicy(sites = listOf("bank.ru"), siteNodeRoutes = mapOf("bank.ru" to second.routeId), countryRuleSets = sets)
        val config = JSONObject(RoutingConfig.build(listOf(node, second), 0, policy))
        val dns = config.getJSONObject("dns")
        val rules = dns.getJSONArray("rules")
        assertEquals("bank.ru", rules.getJSONObject(0).getJSONArray("domain").getString(0))
        assertEquals("dns-route-proxy-1", rules.getJSONObject(0).getString("server"))
        assertEquals("dns-direct", rules.getJSONObject(1).getString("server"))
        assertEquals("country-ru-domains", rules.getJSONObject(2).getJSONArray("rule_set").getString(0))
        val servers = dns.getJSONArray("servers")
        val assigned = (0 until servers.length()).map { servers.getJSONObject(it) }.single { it.getString("tag") == "dns-route-proxy-1" }
        assertEquals("proxy-1", assigned.getString("detour"))
        assertEquals("1.1.1.1", assigned.getString("server"))
        assertEquals("proxy-1", config.getJSONObject("route").getJSONArray("rules").getJSONObject(1).getString("outbound"))
    }

    @Test fun countryDoesNotExpandIncludedOrExcludedApplications() {
        for (exclude in listOf(false, true)) {
            val baseline = RoutePolicy(apps = listOf("com.example.app"), excludeSelectedApps = exclude)
            val a = JSONObject(RoutingConfig.build(node, baseline)).getJSONArray("inbounds").getJSONObject(0)
            val b = JSONObject(RoutingConfig.build(node, baseline.copy(countryRuleSets = sets))).getJSONArray("inbounds").getJSONObject(0)
            assertEquals(a.toString(), b.toString())
        }
    }

    @Test fun ipOnlyCountryDoesNotTreatDnsNamesAsIpRangesAndLocalSetsHaveExplicitPaths() {
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy(countryRuleSets = listOf(sets.first()))))
        assertEquals(0, config.getJSONObject("dns").getJSONArray("rules").length())
        val local = config.getJSONObject("route").getJSONArray("rule_set").getJSONObject(0)
        assertEquals("local", local.getString("type"))
        assertEquals("binary", local.getString("format"))
        assertEquals("/private/geoip-ru.srs", local.getString("path"))
        assertFalse(config.toString().contains("http_client"))
    }

    @Test fun bundledFilesMatchPinnedManifestAndAdvertisedCoverage() {
        val directory = File("src/main/assets/country-rules")
        val manifest = JSONObject(File(directory, "manifest.json").readText()).getJSONArray("files")
        val entries = (0 until manifest.length()).map { manifest.getJSONObject(it) }.associateBy { it.getString("name") }
        assertEquals(13, entries.size)
        CountryRouting.countries.forEach { country ->
            listOfNotNull(country.ipAsset, country.domainAsset).forEach { name ->
                val data = File(directory, name).readBytes()
                assertEquals("SRS", String(data.copyOfRange(0, 3), Charsets.US_ASCII))
                assertEquals(entries.getValue(name).getInt("bytes"), data.size)
                val hash = MessageDigest.getInstance("SHA-256").digest(data).joinToString("") { "%02x".format(it) }
                assertEquals(entries.getValue(name).getString("sha256"), hash)
            }
        }
        assertEquals("IP-адреса", CountryRouting.find("de")!!.coverage)
        assertEquals("Домены и IP-адреса", CountryRouting.find("ru")!!.coverage)
    }
}
