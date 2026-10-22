package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class NodePresentationTest {
    @Test fun metadataTitleIsTheOnlySubscriptionNameEvidence() {
        val node = ProxyNode("🇩🇪 Germany fake label", "vless", "provider-name.example", 443, "synthetic")
        assertEquals("Источник подписки не указан", NodePresentation.subscriptionSource(node))
        assertEquals("Подписка: Partner title", NodePresentation.subscriptionSource(node.copy(subscriptionMetadata = SubscriptionMetadata(title = " Partner title "))))
        assertEquals("Подписка: Partner title", NodePresentation.subscriptionSource(node.copy(subscriptionMetadata = SubscriptionMetadata(title = "base64:UGFydG5lciB0aXRsZQ=="))))
        assertNull(NodePresentation.flag(null))
        assertNull(NodePresentation.flag("ZZ"))
        assertEquals("🇩🇪", NodePresentation.flag("de"))
    }

    @Test fun exitCountryRequiresPublicLiteralIpAndIsoCountry() {
        assertEquals(VerifiedNodeCountry("DE", "8.8.8.8"), NodeExitCountryProbe.parse("""{"country":"DE","ip":"8.8.8.8"}"""))
        listOf("127.0.0.1", "10.0.0.1", "192.168.1.1", "100.64.0.1", "192.0.2.1", "198.51.100.2", "203.0.113.7", "fc00::1", "2001:db8::1", "::1", "provider.example").forEach { ip ->
            assertNull(ip, NodeExitCountryProbe.parse("""{"country":"DE","ip":"$ip"}"""))
        }
        assertNull(NodeExitCountryProbe.parse("""{"country":"ZZ","ip":"8.8.8.8"}"""))
        assertNull(NodeExitCountryProbe.parse("""{"country":"DE"}"""))
        assertNull(NodeExitCountryProbe.parse("not JSON"))
    }

    @Test fun measurementPreservesVerifiedCountryDuringRecheck() {
        val country = VerifiedNodeCountry("DE", "8.8.8.8")
        assertEquals(country, NodeMetricsPolicy.beginCheck(NodeMeasurement(verifiedCountry = country)).verifiedCountry)
        val a = ProxyNode("Same name", "vless", "one.example", 443, "one")
        val b = a.copy(raw = "two")
        assertNotEquals(a.routeId, b.routeId)
        val previous = mapOf(a.routeId to NodeMeasurement(verifiedCountry = country))
        assertEquals(previous, NodeMetricsPolicy.applyVerifiedCountry(previous, mapOf(b.routeId to b.raw), a.routeId, a.raw, VerifiedNodeCountry("US", "1.1.1.1")))
        assertEquals(previous, NodeMetricsPolicy.applyVerifiedCountry(previous, mapOf(a.routeId to a.raw), a.routeId, a.raw, null))
        assertEquals(VerifiedNodeCountry("US", "1.1.1.1"), NodeMetricsPolicy.applyVerifiedCountry(previous, mapOf(a.routeId to a.raw), a.routeId, a.raw, VerifiedNodeCountry("US", "1.1.1.1"))[a.routeId]?.verifiedCountry)
    }

    @Test fun displayNameDropsProviderTagsAndKeepsCountryAndTitle() {
        assertEquals("🇳🇱 Нидерланды-13 · Резервный", NodePresentation.displayName("[TOP 03 • 95.0 Mbps] 🇳🇱 Нидерланды-13 · Резервный"))
        assertEquals("Германия-4", NodePresentation.displayName("Германия-4"))
        assertEquals("[only]", NodePresentation.displayName("[only]"))
    }

    @Test fun titleDropsTheFlagThatTheBadgeAlreadyShows() {
        assertEquals("Германия G+ CebCeh-master", NodePresentation.withoutFlag("🇩🇪 Германия G+ CebCeh-master"))
        assertEquals("proxy", NodePresentation.withoutFlag("proxy"))
        assertEquals("🇩🇪", NodePresentation.withoutFlag("🇩🇪"))
    }
}
