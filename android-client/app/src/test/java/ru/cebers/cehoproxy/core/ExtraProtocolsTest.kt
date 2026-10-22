package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ExtraProtocolsTest {
    private val wgPrivate = "yAnz5TF+lXXJte14tji3zlMNq+hd2rYUIgJBgB3fBmk="
    private val wgPublic = "xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg="
    private fun raw(link: String) = JSONObject(NodeShareParser.parse(link).single().raw)

    @Test fun anytlsLinkBecomesAnAnytlsOutboundWithTls() {
        val node = NodeShareParser.parse("anytls://secret-word@any.example:8443/?sni=front.example&insecure=1&alpn=h2#Any").single()
        assertEquals("anytls", node.type)
        assertEquals("Any", node.name)
        assertEquals(8443, node.port)
        val out = JSONObject(node.raw)
        assertEquals("secret-word", out.getString("password"))
        assertEquals("front.example", out.getJSONObject("tls").getString("server_name"))
        assertTrue(out.getJSONObject("tls").getBoolean("insecure"))
    }

    @Test fun hysteriaOneLinkKeepsAuthSpeedAndObfs() {
        val out = raw("hysteria://hy1.example:443?auth=token&peer=front.example&insecure=1&upmbps=30&downmbps=200&obfsParam=mask&alpn=h3#H1")
        assertEquals("hysteria", out.getString("type"))
        assertEquals("token", out.getString("auth_str"))
        assertEquals(30, out.getInt("up_mbps"))
        assertEquals(200, out.getInt("down_mbps"))
        assertEquals("mask", out.getString("obfs"))
        assertEquals("front.example", out.getJSONObject("tls").getString("server_name"))
    }

    @Test fun hysteriaOneOverFakeTcpIsRefusedInsteadOfBroken() {
        assertTrue(NodeShareParser.parse("hysteria://hy1.example:443?protocol=faketcp&auth=token").isEmpty())
    }

    @Test fun wireguardLinkBecomesAnEndpoint() {
        val node = NodeShareParser.parse("wireguard://${wgPrivate.replace("+", "%2B")}@wg.example:51820?publickey=${wgPublic}&address=10.7.0.2/32,fd00::2/128&mtu=1380&reserved=1,2,3#WG").single()
        assertEquals("wireguard", node.type)
        val out = JSONObject(node.raw)
        assertEquals(wgPrivate, out.getString("private_key"))
        val peer = out.getJSONArray("peers").getJSONObject(0)
        assertEquals("wg.example", peer.getString("address"))
        assertEquals(51820, peer.getInt("port"))
        assertEquals(wgPublic, peer.getString("public_key"))
        assertEquals(3, peer.getJSONArray("reserved").length())
        assertEquals(1380, out.getInt("mtu"))
        assertEquals("10.7.0.2/32", out.getJSONArray("address").getString(0))
    }

    @Test fun wireguardConfigFileIsAccepted() {
        val conf = """
            [Interface]
            PrivateKey = $wgPrivate
            Address = 10.8.0.2/32, fd00:8::2/128
            DNS = 1.1.1.1
            MTU = 1280

            [Peer]
            PublicKey = $wgPublic
            AllowedIPs = 0.0.0.0/0, ::/0
            Endpoint = vpn.example:51821
            PersistentKeepalive = 25
        """.trimIndent()
        val node = NodeShareParser.parse(conf).single()
        assertEquals("wireguard", node.type)
        assertEquals(51821, node.port)
        assertEquals("vpn.example", node.server)
        assertEquals(1280, JSONObject(node.raw).getInt("mtu"))
    }

    @Test fun sshLinkNeedsLoginAndSecret() {
        val out = raw("ssh://tunnel@ssh.example:2222?private_key=KEYDATA#Jump")
        assertEquals("ssh", out.getString("type"))
        assertEquals("tunnel", out.getString("user"))
        assertEquals("KEYDATA", out.getString("private_key"))
        assertTrue(NodeShareParser.parse("ssh://tunnel@ssh.example:2222").isEmpty())
        val withPassword = raw("ssh://tunnel:" + "p%40ss" + "@ssh.example#Jump")
        assertEquals("p@ss", withPassword.getString("password"))
        assertEquals(22, withPassword.getInt("server_port"))
    }

    @Test fun socksFourSchemesKeepTheirVersion() {
        val config = JSONObject(RoutingConfig.build(NodeShareParser.parse("socks4://old.example:1080#S4").single(), RoutePolicy()))
        assertEquals("4", config.getJSONArray("outbounds").getJSONObject(0).getString("version"))
        val four = JSONObject(RoutingConfig.build(NodeShareParser.parse("socks4a://old.example:1080#S4a").single(), RoutePolicy()))
        assertEquals("4a", four.getJSONArray("outbounds").getJSONObject(0).getString("version"))
    }

    @Test fun wireguardNodeGoesToEndpointsAndStaysRoutable() {
        val node = NodeShareParser.parse("wg://${wgPrivate.replace("+", "%2B")}@wg.example:51820?publickey=${wgPublic}&address=10.7.0.2#WG").single()
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy()))
        assertEquals("wireguard", config.getJSONArray("endpoints").getJSONObject(0).getString("type"))
        assertEquals("proxy", config.getJSONArray("endpoints").getJSONObject(0).getString("tag"))
        assertEquals("proxy", config.getJSONObject("route").getString("final"))
        assertTrue((0 until config.getJSONArray("outbounds").length()).none { config.getJSONArray("outbounds").getJSONObject(it).getString("type") == "wireguard" })
    }

    @Test fun singBoxJsonWithEndpointsAndNewOutboundTypes() {
        val json = """{"outbounds":[{"type":"anytls","tag":"a","server":"a.example","server_port":443,"password":"x","tls":{"enabled":true}},
            {"type":"hysteria","tag":"h","server":"h.example","server_port":443,"up_mbps":10,"down_mbps":50,"auth_str":"t","tls":{"enabled":true}},
            {"type":"ssh","tag":"s","server":"s.example","server_port":22,"user":"u","password":"p"},
            {"type":"direct","tag":"direct"}],
            "endpoints":[{"type":"wireguard","tag":"w","address":["10.0.0.2/32"],"private_key":"$wgPrivate","peers":[{"address":"w.example","port":51820,"public_key":"$wgPublic","allowed_ips":["0.0.0.0/0"]}]}]}"""
        assertEquals(setOf("anytls", "hysteria", "ssh", "wireguard"), NodeShareParser.parse(json).map { it.type }.toSet())
    }

    @Test fun oldSingBoxWireguardOutboundIsConverted() {
        val json = """{"outbounds":[{"type":"wireguard","tag":"w","server":"w.example","server_port":51820,"local_address":["10.0.0.2/32"],"private_key":"$wgPrivate","peer_public_key":"$wgPublic"}]}"""
        val node = NodeShareParser.parse(json).single()
        assertEquals("wireguard", node.type)
        assertEquals("w.example", node.server)
        assertEquals(wgPublic, JSONObject(node.raw).getJSONArray("peers").getJSONObject(0).getString("public_key"))
    }

    @Test fun xrayWireguardIsConverted() {
        val json = """{"outbounds":[{"protocol":"wireguard","tag":"xw","settings":{"secretKey":"$wgPrivate","address":["10.0.0.2/32"],"peers":[{"publicKey":"$wgPublic","endpoint":"w.example:51820"}],"mtu":1280}}]}"""
        val node = NodeShareParser.parse(json).single()
        assertEquals("wireguard", node.type)
        assertEquals(51820, node.port)
    }

    @Test fun clashCoversTuicPasswordAnytlsHysteriaSshAndWireguard() {
        val yaml = """
            proxies:
              - {name: tuic-1, type: tuic, server: t.example, port: 443, uuid: 11111111-2222-3333-4444-555555555555, password: tp, sni: front.example}
              - {name: any-1, type: anytls, server: a.example, port: 443, password: ap, sni: front.example, skip-cert-verify: true}
              - {name: hy-1, type: hysteria, server: h.example, port: 443, auth-str: tok, up: 20 Mbps, down: 100 Mbps, obfs: mask, sni: front.example}
              - {name: ssh-1, type: ssh, server: s.example, port: 22, username: tunnel, password: sp}
              - {name: wg-1, type: wireguard, server: w.example, port: 51820, ip: 10.0.0.2, private-key: $wgPrivate, public-key: $wgPublic, mtu: 1280}
        """.trimIndent()
        val nodes = NodeShareParser.parse(yaml)
        assertEquals(listOf("tuic", "anytls", "hysteria", "ssh", "wireguard"), nodes.map { it.type })
        assertEquals("tp", JSONObject(nodes[0].raw).getString("password"))
        assertTrue(JSONObject(nodes[1].raw).getJSONObject("tls").getBoolean("insecure"))
        assertEquals(20, JSONObject(nodes[2].raw).getInt("up_mbps"))
        assertEquals("tunnel", JSONObject(nodes[3].raw).getString("user"))
        assertEquals("10.0.0.2/32", JSONObject(nodes[4].raw).getJSONArray("address").getString(0))
    }

    @Test fun unsupportedLinkSchemesAreNamedForTheUser() {
        assertEquals(listOf("ssr"), NodeShareParser.unsupportedTypes("ssr://c3ludGhldGlj"))
    }

    @Test fun wrapperLinksReturnTheRealSubscriptionAddress() {
        val sub = "https://sub.example/api/token123"
        val enc = java.net.URLEncoder.encode(sub, "UTF-8")
        listOf(
            "sing-box://import-remote-profile?url=$enc#My",
            "clash://install-config?url=$enc&name=x",
            "clashmeta://install-config?url=$enc",
            "v2rayng://install-config?url=$enc#x",
            "hiddify://import/$sub#Name",
            "happ://add/$sub",
            "v2raytun://import/$enc",
            "streisand://import/$sub",
            "flclash://install-config?url=$enc",
            "shadowrocket://add/sub://" + java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(sub.toByteArray()),
        ).forEach { assertEquals(it, sub, SubscriptionInput.unwrap(it)) }
    }

    @Test fun wrapperUnwrapLeavesOrdinaryInputAlone() {
        listOf("https://sub.example/list", "vless://id@h.example:443#n", "happ://crypt3/abcdef", "plain text", "trojan://p@h.example:443\nvless://i@h2.example:443")
            .forEach { assertEquals(it, SubscriptionInput.unwrap(it)) }
        assertNull(null)
    }

    @Test fun vmessLinkKeepsTheAllowInsecureFlag() {
        val json = """{"v":"2","ps":"vm","add":"vm.example","port":"443","id":"11111111-1111-1111-1111-111111111111","aid":"0","net":"tcp","tls":"tls","sni":"front.example","allowInsecure":"1"}"""
        val link = "vmess://" + java.util.Base64.getEncoder().encodeToString(json.toByteArray())
        val out = JSONObject(RoutingConfig.build(NodeShareParser.parse(link).single(), RoutePolicy())).getJSONArray("outbounds").getJSONObject(0)
        assertTrue(out.getJSONObject("tls").getBoolean("insecure"))
    }
}
