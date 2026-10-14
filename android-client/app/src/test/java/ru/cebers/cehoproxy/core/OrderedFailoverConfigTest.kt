package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class OrderedFailoverConfigTest {
    private val nodes = NodeShareParser.parse("socks5://127.0.0.1:10001#First\nsocks5://127.0.0.1:10002#Second")

    @Test fun probeRulePrecedesAssignedRulesWithoutChangingThem() {
        val policy = RoutePolicy(autoRotationEnabled = true, appNodeRoutes = mapOf("app.test" to nodes[1].routeId), siteNodeRoutes = mapOf("site.test" to nodes[0].routeId))
        val config = JSONObject(OrderedFailoverConfig.withProbe(RoutingConfig.build(nodes, 1, policy), 19001))
        val rules = config.getJSONObject("route").getJSONArray("rules")
        assertEquals(OrderedFailoverConfig.PROBE_TAG, rules.getJSONObject(0).getString("outbound"))
        val assigned = (1 until rules.length()).map { rules.getJSONObject(it) }.filter { it.has("outbound") }
        assertEquals(listOf("proxy-0", "proxy-1"), assigned.map { it.getString("outbound") })
        assertEquals(listOf("proxy-0", "proxy-1"), OrderedFailoverConfig.candidates(config.toString()))
        assertEquals(OrderedFailoverConfig.GLOBAL_TAG, config.getJSONObject("route").getString("final"))
    }

    @Test fun manualSingleNodeStillAllowsPerNodeExitLookupWithoutChangingGlobalRoute() {
        val config = JSONObject(OrderedFailoverConfig.withProbe(RoutingConfig.build(nodes.take(1), 0, RoutePolicy()), 19001))
        assertEquals("proxy", config.getJSONObject("route").getString("final"))
        assertEquals(listOf("proxy"), OrderedFailoverConfig.candidates(config.toString()))
        val probe = config.getJSONArray("inbounds").getJSONObject(1)
        assertEquals("127.0.0.1", probe.getString("listen"))
    }
}
