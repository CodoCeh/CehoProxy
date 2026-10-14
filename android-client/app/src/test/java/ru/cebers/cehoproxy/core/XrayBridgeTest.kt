package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class XrayBridgeTest {
    private val uuid = "11111111-1111-1111-1111-111111111111"
    private val xhttp = NodeShareParser.parse("vless://$uuid@203.0.113.1:9443?encryption=none&security=reality&sni=example.com&fp=qq&pbk=AAAA&sid=ab&type=xhttp&mode=auto#xh").single()
    private val kcp = NodeShareParser.parse("vless://$uuid@203.0.113.2:443?security=tls&sni=example.com&alpn=h2,http/1.1&type=kcp&headerType=srtp&seed=s1#kc").single()

    @Test fun detectsOnlyVlessNodesOnXrayTransports() {
        assertTrue(XrayBridge.needsXray(xhttp))
        assertTrue(XrayBridge.needsXray(kcp))
        assertFalse(XrayBridge.needsXray(NodeShareParser.parse("vless://$uuid@203.0.113.3:443?type=ws&security=none#ws").single()))
        assertFalse(XrayBridge.needsXray(NodeShareParser.parse("trojan://pass@203.0.113.4:443#tr").single()))
    }

    @Test fun buildsSocksInboundPerNodeAndMatchingRoute() {
        val config = JSONObject(XrayBridge.config(listOf(xhttp, kcp), mapOf(xhttp.routeId to 10001, kcp.routeId to 10002)))
        val inbounds = config.getJSONArray("inbounds")
        assertEquals(listOf(10001, 10002), (0 until inbounds.length()).map { inbounds.getJSONObject(it).getInt("port") })
        assertEquals("127.0.0.1", inbounds.getJSONObject(0).getString("listen"))
        val rules = config.getJSONObject("routing").getJSONArray("rules")
        assertEquals("in-0", rules.getJSONObject(0).getJSONArray("inboundTag").getString(0))
        assertEquals("out-0", rules.getJSONObject(0).getString("outboundTag"))
    }

    @Test fun xhttpOutboundCarriesRealityAndXhttpSettings() {
        val out = JSONObject(XrayBridge.config(listOf(xhttp), mapOf(xhttp.routeId to 10001))).getJSONArray("outbounds").getJSONObject(0)
        assertEquals("vless", out.getString("protocol"))
        val vnext = out.getJSONObject("settings").getJSONArray("vnext").getJSONObject(0)
        assertEquals("203.0.113.1", vnext.getString("address"))
        assertEquals(9443, vnext.getInt("port"))
        assertEquals(uuid, vnext.getJSONArray("users").getJSONObject(0).getString("id"))
        val stream = out.getJSONObject("streamSettings")
        assertEquals("xhttp", stream.getString("network"))
        assertEquals("reality", stream.getString("security"))
        val reality = stream.getJSONObject("realitySettings")
        assertEquals("example.com", reality.getString("serverName"))
        assertEquals("AAAA", reality.getString("publicKey"))
        assertEquals("qq", reality.getString("fingerprint"))
        assertEquals("auto", stream.getJSONObject("xhttpSettings").getString("mode"))
        assertEquals("/", stream.getJSONObject("xhttpSettings").getString("path"))
    }

    @Test fun kcpOutboundCarriesTlsAndKcpSettings() {
        val out = JSONObject(XrayBridge.config(listOf(kcp), mapOf(kcp.routeId to 10002))).getJSONArray("outbounds").getJSONObject(0)
        val stream = out.getJSONObject("streamSettings")
        assertEquals("kcp", stream.getString("network"))
        assertEquals("tls", stream.getString("security"))
        assertEquals("srtp", stream.getJSONObject("kcpSettings").getJSONObject("header").getString("type"))
        assertEquals("s1", stream.getJSONObject("kcpSettings").getString("seed"))
        assertEquals("h2", stream.getJSONObject("tlsSettings").getJSONArray("alpn").getString(0))
    }

    @Test fun routingBuildUsesLocalSocksOutboundForXrayNode() {
        val config = JSONObject(RoutingConfig.build(xhttp, RoutePolicy(xrayPorts = mapOf(xhttp.routeId to 10001))))
        val proxy = (0 until config.getJSONArray("outbounds").length()).map { config.getJSONArray("outbounds").getJSONObject(it) }
            .first { it.optString("tag") == "proxy" }
        assertEquals("socks", proxy.getString("type"))
        assertEquals("127.0.0.1", proxy.getString("server"))
        assertEquals(10001, proxy.getInt("server_port"))
    }
}
