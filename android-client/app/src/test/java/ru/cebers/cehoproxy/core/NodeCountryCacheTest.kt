package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class NodeCountryCacheTest {
    @Test fun cachedExitSurvivesSerializationUntilExactly24Hours() {
        val checked = 10_000L
        val country = VerifiedNodeCountry("DE", "8.8.8.8", checked)
        val saved = NodeCountryCachePolicy.encode(country)
        assertEquals(country, NodeCountryCachePolicy.decode(saved, checked + 1))
        assertEquals(country, NodeCountryCachePolicy.decode(saved, checked + NodeCountryCachePolicy.TTL_MILLIS - 1))
        assertNull(NodeCountryCachePolicy.decode(saved, checked + NodeCountryCachePolicy.TTL_MILLIS))
        assertNull(NodeCountryCachePolicy.decode(saved, checked - 1))
        assertNull(NodeCountryCachePolicy.decode("""{"country":"DE","ip":"8.8.8.8"}""", checked))
    }

    @Test fun countryForOldNodeIdentityCannotBeAppliedToReplacement() {
        val old = ProxyNode("same", "socks", "one.example", 1080, "old")
        val replacement = old.copy(raw = "replacement")
        val country = VerifiedNodeCountry("DE", "8.8.8.8", 1000)
        val restored = mapOf(old.routeId to NodeMeasurement(verifiedCountry = country))
        val applied = NodeMetricsPolicy.applyVerifiedCountry(restored, mapOf(replacement.routeId to replacement.raw), old.routeId, old.raw, country)
        assertNull(applied[replacement.routeId]?.verifiedCountry)
        assertEquals(restored, applied)
    }
}
