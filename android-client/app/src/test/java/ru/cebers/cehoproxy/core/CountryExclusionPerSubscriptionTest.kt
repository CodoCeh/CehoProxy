package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class CountryExclusionPerSubscriptionTest {
    private fun node(name: String, id: String, subscriptionHost: String): ProxyNode =
        NodeShareParser.parse("vless://$id@$id.example:443#$name").single()
            .copy(subscriptionMetadata = SubscriptionMetadata(sourceHost = subscriptionHost))

    private val germanyA = node("Германия-1", "11111111-1111-1111-1111-111111111111", "a.example")
    private val germanyB = node("Германия-2", "22222222-2222-2222-2222-222222222222", "b.example")
    private val franceB = node("Франция-1", "33333333-3333-3333-3333-333333333333", "b.example")

    @Test fun excludingCountryInOneSubscriptionKeepsItInAnother() {
        val excluded = mapOf("a.example" to setOf("DE"))
        val allowed = RoutingConfig.allowedNodesFor(listOf(germanyA, germanyB, franceB), excluded)
        assertEquals(listOf(germanyB.raw, franceB.raw), allowed.map { it.raw })
    }

    @Test fun countryExclusionOnlyMatchesItsOwnSubscription() {
        val excluded = mapOf("a.example" to setOf("DE"))
        assertTrue(RoutingConfig.countryExcluded(germanyA, excluded))
        assertFalse(RoutingConfig.countryExcluded(germanyB, excluded))
        assertFalse(RoutingConfig.countryExcluded(franceB, excluded))
    }

    @Test fun routingConfigContainsOnlyNodesAllowedInEachSubscription() {
        val config = RoutingConfig.build(listOf(germanyA, germanyB), 1, RoutePolicy(excludedCountriesBySubscription = mapOf("a.example" to setOf("DE"))))
        assertFalse(config.contains(germanyA.raw.substringAfter("vless://").substringBefore("@")))
        assertTrue(config.contains(germanyB.raw.substringAfter("vless://").substringBefore("@")))
    }
}
