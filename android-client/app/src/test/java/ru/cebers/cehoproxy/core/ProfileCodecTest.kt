package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ProfileCodecTest {
    @Test fun roundTripsJsonOutboundNodeAndProfileRules() {
        val rawOutbound = """{"type":"vless","tag":"Work node","server":"edge.example","server_port":8443,"uuid":"synthetic-id"}"""
        val node = ProxyNode("Work node", "vless", "edge.example", 8443, rawOutbound, 1_800_000_000L)
        val profile = ClientProfile(listOf(node), selected = 0, apps = listOf("com.example.mail"), sites = listOf("mail.example"), onlySites = true, excludeSelectedApps = true)

        val decoded = ProfileCodec.decode(ProfileCodec.encode(profile))

        assertEquals(node, decoded.selectedNode)
        assertEquals(profile.apps, decoded.apps)
        assertEquals(profile.sites, decoded.sites)
        assertTrue(decoded.onlySites)
        assertTrue(decoded.excludeSelectedApps)
        assertFalse(decoded.expiryRemindersEnabled)
    }

    @Test fun routeAssignmentsAndAutoRotationSurviveProfileRoundTrip() {
        val node = NodeShareParser.parse("vless://synthetic-id@edge.example:443").single()
        val profile = ClientProfile(
            nodes = listOf(node),
            appNodeRoutes = mapOf("com.example.chat" to node.routeId),
            siteNodeRoutes = mapOf("chat.example" to node.routeId),
            autoRotationEnabled = true,
            autoRotationIntervalSeconds = 300,
        )
        val decoded = ProfileCodec.decode(ProfileCodec.encode(profile))
        assertEquals(profile.appNodeRoutes, decoded.appNodeRoutes)
        assertEquals(profile.siteNodeRoutes, decoded.siteNodeRoutes)
        assertTrue(decoded.autoRotationEnabled)
        assertEquals(300, decoded.autoRotationIntervalSeconds)
    }

    @Test fun autoExcludedNodesSurviveProfileRoundTrip() {
        val node = NodeShareParser.parse("vless://synthetic-id@edge.example:443").single()
        val profile = ClientProfile(nodes = listOf(node), autoExcludedRouteIds = listOf(node.routeId))
        assertEquals(listOf(node.routeId), ProfileCodec.decode(ProfileCodec.encode(profile)).autoExcludedRouteIds)
        assertEquals(listOf("RU"), ProfileCodec.decode(ProfileCodec.encode(profile.copy(excludedCountries = listOf("ru")))).excludedCountries)
    }

    @Test fun persistsSubscriptionMetadataAndReminderPreference() {
        val metadata = SubscriptionMetadata(1_800_000_000L, 100, 200, 500, "Plan", "Details", "News", "https://t.me/support", "https://example.org", "Open", "https://example.org", "tg://resolve?domain=renew", true)
        val node = NodeShareParser.parse("vless://synthetic-id@edge.example:443").single().copy(subscriptionExpiresAt = metadata.expiresAtEpochSeconds, subscriptionMetadata = metadata)
        val profile = ClientProfile(nodes = listOf(node), expiryRemindersEnabled = false)

        val decoded = ProfileCodec.decode(ProfileCodec.encode(profile))

        assertEquals(metadata, decoded.selectedNode?.subscriptionMetadata)
        assertFalse(decoded.expiryRemindersEnabled)
    }

    @Test fun importedSingBoxConfigExposesProxyOutboundsAsRouteChoices() {
        val config = """{"inbounds":[{"type":"tun"}],"outbounds":[{"type":"vless","tag":"one","server":"one.example","server_port":443,"uuid":"one-id"},{"type":"trojan","tag":"two","server":"two.example","server_port":443,"password":"two-pass"},{"type":"direct","tag":"direct"}],"route":{"final":"one"}}"""
        val imported = ProfileImportPolicy.useSingBoxConfig(ClientProfile(), config)
        assertEquals(listOf("one", "two"), imported.nodes.map { it.name })
        assertEquals(config, imported.importedConfig)
    }

    @Test fun preservesZeroUsageCounters() {
        val metadata = SubscriptionMetadata(uploadBytes = 0, downloadBytes = 0, totalBytes = 0)
        val node = NodeShareParser.parse("vless://synthetic-id@edge.example:443").single().copy(subscriptionMetadata = metadata)
        val decoded = ProfileCodec.decode(ProfileCodec.encode(ClientProfile(nodes = listOf(node))))
        assertEquals(0L, decoded.selectedNode?.subscriptionMetadata?.usedBytes)
        assertEquals(0L, decoded.selectedNode?.subscriptionMetadata?.totalBytes)
    }

    @Test fun readsLegacySubscriptionOnlyProfile() {
        val decoded = ProfileCodec.decode("""{"subscription":"vless://synthetic-id@edge.example:443","selected":0,"apps":["com.example.mail"],"sites":["mail.example"],"onlySites":true}""")

        assertEquals("edge.example", decoded.selectedNode?.server)
        assertEquals(listOf("com.example.mail"), decoded.apps)
        assertEquals(listOf("mail.example"), decoded.sites)
        assertTrue(decoded.onlySites)
    }

    @Test fun importingSingBoxConfigPreservesAppAllowlistAndClearsProfileOnlyRules() {
        val existingNode = NodeShareParser.parse("vless://synthetic-id@edge.example:443").single()
        val profile = ClientProfile(listOf(existingNode), apps = listOf("com.example.mail"), sites = listOf("mail.example"), onlySites = true)

        val imported = ProfileImportPolicy.useSingBoxConfig(profile, "{\"outbounds\":[]}")

        assertEquals(profile.apps, imported.apps)
        assertTrue(imported.sites.isEmpty())
        assertFalse(imported.onlySites)
        assertEquals(existingNode, imported.selectedNode)
        assertEquals("{\"outbounds\":[]}", imported.importedConfig)
    }

    @Test fun stalePersistedConnectedStatusNeedsLiveService() {
        assertFalse(VpnStatusPolicy.isConnected("connected", liveServiceConnected = false))
        assertTrue(VpnStatusPolicy.isConnected("connected", liveServiceConnected = true))
        assertFalse(VpnStatusPolicy.isConnected("starting", liveServiceConnected = true))
        assertFalse(VpnStatusPolicy.isStarting("starting", liveServiceInstance = false))
        assertTrue(VpnStatusPolicy.isStarting("starting", liveServiceInstance = true))
    }
}
