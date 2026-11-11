package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class LanBypassTest {
    private val node = NodeShareParser.parse("vless://synthetic-id@edge.example:443#test").single()

    private fun tun(config: String): JSONObject {
        val inbounds = JSONObject(config).getJSONArray("inbounds")
        return (0 until inbounds.length()).map { inbounds.getJSONObject(it) }.first { it.optString("type") == "tun" }
    }

    private fun excludes(tun: JSONObject): List<String> = tun.optJSONArray("route_exclude_address")?.let { a -> (0 until a.length()).map { a.getString(it) } } ?: emptyList()

    @Test fun builtConfigSendsHomeNetworksPastTunnelButKeepsOwnTunSubnet() {
        val excluded = excludes(tun(RoutingConfig.build(node, RoutePolicy())))
        assertTrue("192.168.0.0/16" in excluded)
        assertTrue("10.0.0.0/8" in excluded)
        assertTrue("169.254.0.0/16" in excluded)
        assertFalse(excluded.any { it.startsWith("172.19.") || it == "172.16.0.0/12" || it == "172.16.0.0/14" })
    }

    @Test fun importedConfigGetsBypassWithoutTouchingItsOwnAddressOrExistingExcludes() {
        val imported = JSONObject().put("inbounds", JSONArray().put(JSONObject().put("type", "tun")
            .put("address", JSONArray().put("10.8.0.1/30")).put("route_exclude_address", JSONArray().put("203.0.113.0/24"))))
            .put("outbounds", JSONArray().put(JSONObject().put("type", "direct").put("tag", "direct"))).toString()
        val excluded = excludes(tun(RoutingConfig.applyToImported(imported, RoutePolicy())))
        assertTrue("203.0.113.0/24" in excluded)
        assertTrue("192.168.0.0/16" in excluded)
        assertFalse("10.0.0.0/8" in excluded)
    }
}
