package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ProbeTagMappingTest {
    private fun node(name: String, uuid: String) = NodeShareParser.parse("vless://$uuid@$uuid.example:443#$name").single()

    @Test fun excludedCountryDoesNotShiftTagsOfOtherNodes() {
        val de = node("Германия-1", "11111111-1111-1111-1111-111111111111")
        val nl = node("Нидерланды-1", "22222222-2222-2222-2222-222222222222")
        val fr = node("Франция-1", "33333333-3333-3333-3333-333333333333")
        val tags = RoutingConfig.tagsByRaw(listOf(de, nl, fr), mapOf(NodePresentation.subscriptionKey(de) to setOf("DE")))
        assertEquals("proxy-0", tags[nl.raw])
        assertEquals("proxy-1", tags[fr.raw])
        assertNull(tags[de.raw])
    }

    @Test fun tagsMatchOutboundsInRoutingConfig() {
        val de = node("Германия-1", "11111111-1111-1111-1111-111111111111")
        val nl = node("Нидерланды-1", "22222222-2222-2222-2222-222222222222")
        val config = org.json.JSONObject(RoutingConfig.build(listOf(de, nl), 1, RoutePolicy(excludedCountriesBySubscription = mapOf(NodePresentation.subscriptionKey(de) to setOf("DE")))))
        val tags = OrderedFailoverConfig.proxyTags(config.toString())
        assertEquals(listOf(RoutingConfig.tagsByRaw(listOf(de, nl), mapOf(NodePresentation.subscriptionKey(de) to setOf("DE")))[nl.raw]), tags)
    }
}
