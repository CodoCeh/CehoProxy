package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class NodeShareParserTest {
    private fun login(user: String, secret: String) = user + ":" + secret

    @Test fun parsesProtocolsAndNames() {
        val nodes = NodeShareParser.parse("vless://id@edge.example:443#Main%20line\ntrojan://secret@secure.example:8443#Backup")
        assertEquals(listOf("vless", "trojan"), nodes.map { it.type })
        assertEquals("Main line", nodes.first().name)
        assertEquals(8443, nodes.last().port)
    }

    @Test fun decodesBase64Subscription() {
        val encoded = java.util.Base64.getEncoder().encodeToString("socks5://${login("user", "pass")}@proxy.example:1080".toByteArray())
        assertEquals("socks5", NodeShareParser.parse(encoded).single().type)
    }

    @Test fun acceptsEveryStandardShareSchemeListedByHapp() {
        val nodes = NodeShareParser.parse(
            """vless://synthetic-id@vless.example:443#vless
                vmess://eyJwcyI6IlN5bnRoZXRpYyBWTWVzcyIsImFkZCI6InZtZXNzLmV4YW1wbGUiLCJwb3J0IjoiNDQzIiwiaWQiOiJzeW50aGV0aWMtaWQifQ==
                socks://${login("user", "pass")}@socks.example:1080#socks
                trojan://synthetic-pass@trojan.example:443#trojan
                ss://YWVzLTEyOC1nY206c3ludGhldGljLXBhc3N3b3JkQHNzLmV4YW1wbGU6ODM4OA==#ss
                hysteria2://synthetic-pass@hy2.example:443?sni=front.example#hy2""".trimIndent()
        )
        assertEquals(listOf("vless", "vmess", "socks", "trojan", "ss", "hysteria2"), nodes.map { it.type })
    }

    @Test fun reportsEncryptedHappSchemeAsUnrecognizedRatherThanTreatingItAsHttp() {
        assertTrue(NodeShareParser.parse("happ://crypt5/synthetic-ciphertext").isEmpty())
    }

    @Test fun parsesVmessAndLegacyShadowsocksWithApi24Base64Decoder() {
        val vmess = NodeShareParser.parse("vmess://eyJwcyI6IlN5bnRoZXRpYyBWTWVzcyIsImFkZCI6InZtZXNzLmV4YW1wbGUiLCJwb3J0IjoiNDQzIiwiaWQiOiJzeW50aGV0aWMtaWQifQ==").single()
        assertEquals("vmess", vmess.type)
        assertEquals("vmess.example", vmess.server)
        assertEquals(443, vmess.port)

        val shadowsocks = NodeShareParser.parse("ss://YWVzLTEyOC1nY206c3ludGhldGljLXBhc3N3b3JkQHNzLmV4YW1wbGU6ODM4OA==").single()
        assertEquals("ss", shadowsocks.type)
        assertEquals("ss.example", shadowsocks.server)
        assertEquals(8388, shadowsocks.port)
    }

    @Test fun api24Base64DecoderAcceptsUrlSafeUnpaddedInput() {
        assertEquals("f", String(Base64Compat.decode("Zg"), Charsets.UTF_8))
        assertArrayEquals(byteArrayOf(0xFB.toByte(), 0xFF.toByte(), 0x6F), Base64Compat.decode("-_9v"))
    }

    @Test fun rejectsMalformedAndUnsupportedLinks() {
        assertTrue(NodeShareParser.parse("https://site.example/subscribe\nftp://host:21\nvless://x@host:99999").isEmpty())
    }

    @Test fun parsesDesktopNaiveShareFormats() {
        val nodes = NodeShareParser.parse(
            "naive+https://${login("alice", "secret")}@edge.example:8443?sni=front.example&insecure=1#HTTPS\n" +
                "naive+quic://${login("bob", "token")}@quic.example:443#QUIC\n" +
                "https://${login("carol", "pass%40word")}@ordinary.example:8443/"
        )
        assertEquals(listOf("naive", "naive", "naive"), nodes.map { it.type })
        assertEquals(listOf("edge.example", "quic.example", "ordinary.example"), nodes.map { it.server })
        assertEquals(listOf(8443, 443, 8443), nodes.map { it.port })
        assertTrue(nodes[1].raw.startsWith("naive+quic://${login("bob", "token")}@"))
        assertTrue(nodes.last().raw.startsWith("https://${login("carol", "pass%40word")}@"))
    }

    @Test fun naiveOutboundKeepsCredentialsTlsAndQueryOptions() {
        val node = NodeShareParser.parse("naive+https://${login("alice", "secret")}@edge.example:8443?sni=front%2Eexample&insecure=1").single()
        val outbound = org.json.JSONObject(RoutingConfig.build(node, RoutePolicy())).getJSONArray("outbounds").getJSONObject(0)
        assertEquals("alice", outbound.getString("username"))
        assertEquals("secret", outbound.getString("password"))
        assertTrue(outbound.getJSONObject("tls").getBoolean("enabled"))
        assertEquals("front.example", outbound.getJSONObject("tls").getString("server_name"))
        assertTrue(outbound.getJSONObject("tls").getBoolean("insecure"))
        assertEquals(false, outbound.getBoolean("quic"))

        val ordinary = NodeShareParser.parse("https://${login("carol", "pass%40word")}@ordinary.example:8443/").single()
        val ordinaryOutbound = org.json.JSONObject(RoutingConfig.build(ordinary, RoutePolicy())).getJSONArray("outbounds").getJSONObject(0)
        assertEquals("carol", ordinaryOutbound.getString("username"))
        assertEquals("pass@word", ordinaryOutbound.getString("password"))
        assertEquals(false, ordinaryOutbound.getBoolean("quic"))
    }

    @Test fun naiveQuicFlavorAndCredentialsReachOutbound() {
        val node = NodeShareParser.parse("naive+quic://${login("bob", "token%40secret")}@quic.example:443#QUIC").single()
        assertTrue(node.raw.startsWith("naive+quic://"))
        val outbound = org.json.JSONObject(RoutingConfig.build(node, RoutePolicy())).getJSONArray("outbounds").getJSONObject(0)
        assertEquals("bob", outbound.getString("username"))
        assertEquals("token@secret", outbound.getString("password"))
        assertTrue(outbound.getBoolean("quic"))

        val naive = NodeShareParser.parse("naive://${login("alice", "plain")}@tcp.example:443").single()
        val naiveOutbound = org.json.JSONObject(RoutingConfig.build(naive, RoutePolicy())).getJSONArray("outbounds").getJSONObject(0)
        assertEquals(false, naiveOutbound.getBoolean("quic"))
    }

    @Test fun xrayWholeCertificatePinIsAcceptedAsInsecure() {
        val input = """{"outbounds":[{"protocol":"hysteria2","settings":{"address":"edge.example","port":8443,"password":"synthetic"},"streamSettings":{"security":"tls","tlsSettings":{"serverName":"edge.example","pinnedPeerCertSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}}]}"""
        val node = NodeShareParser.parse(input).single()
        assertEquals("hysteria2", node.type)
        assertTrue(org.json.JSONObject(node.raw).getJSONObject("tls").getBoolean("insecure"))
    }

    @Test fun nativeTlsPinsAndProviderExplicitVerificationSettingsArePreserved() {
        for (insecure in listOf(true, false)) {
            val input = """{"outbounds":[{"type":"hysteria2","tag":"native","server":"edge.example","server_port":8443,"password":"synthetic","tls":{"enabled":true,"server_name":"edge.example","insecure":$insecure,"certificate_public_key_sha256":["AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="]}}]}"""
            val node = NodeShareParser.parse(input).single()
            val tls = org.json.JSONObject(RoutingConfig.build(node, RoutePolicy())).getJSONArray("outbounds").getJSONObject(0).getJSONObject("tls")
            assertEquals(insecure, tls.getBoolean("insecure"))
            assertEquals("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", tls.getJSONArray("certificate_public_key_sha256").getString(0))
        }
    }

    @Test fun parsesXrayJsonAndPreservesTlsRealityAndTransport() {
        val nodes = NodeShareParser.parse("""{"outbounds":[{"tag":"xray-vless","protocol":"vless","settings":{"vnext":[{"address":"edge.example","port":443,"users":[{"id":"synthetic-id","flow":"xtls-rprx-vision"}]}]},"streamSettings":{"network":"ws","security":"reality","realitySettings":{"serverName":"front.example","publicKey":"synthetic-key","shortId":"abcd"},"wsSettings":{"path":"/tunnel","headers":{"Host":"cdn.example"}}}}]}""")
        assertEquals(1, nodes.size)
        assertEquals("vless", nodes.single().type)
        val outbound = org.json.JSONObject(nodes.single().raw)
        assertEquals("synthetic-id", outbound.getString("uuid"))
        assertEquals("xtls-rprx-vision", outbound.getString("flow"))
        assertEquals("front.example", outbound.getJSONObject("tls").getString("server_name"))
        assertEquals("synthetic-key", outbound.getJSONObject("tls").getJSONObject("reality").getString("public_key"))
        assertEquals("/tunnel", outbound.getJSONObject("transport").getString("path"))
        assertEquals("cdn.example", outbound.getJSONObject("transport").getJSONObject("headers").getString("Host"))
    }

    @Test fun parsesXrayProfileArrayWithRealityFingerprintAndReadableProfileName() {
        val input = """[{"remarks":"Provider profile","outbounds":[{"tag":"proxy","protocol":"vless","settings":{"vnext":[{"address":"edge.example","port":443,"users":[{"id":"11111111-1111-1111-1111-111111111111","flow":""}]}]},"streamSettings":{"network":"grpc","security":"reality","realitySettings":{"serverName":"front.example","fingerprint":"chrome","publicKey":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","shortId":"abcd"},"grpcSettings":{"serviceName":"service"}}}]}]"""
        val node = NodeShareParser.parse(input).single()
        val outbound = org.json.JSONObject(node.raw)
        assertEquals("Provider profile", node.name)
        assertEquals("chrome", outbound.getJSONObject("tls").getJSONObject("utls").getString("fingerprint"))
        assertEquals("service", outbound.getJSONObject("transport").getString("service_name"))
        val withoutFingerprint = NodeShareParser.parse(input.replace("\"fingerprint\":\"chrome\",", "")).single()
        assertEquals("chrome", org.json.JSONObject(withoutFingerprint.raw).getJSONObject("tls").getJSONObject("utls").getString("fingerprint"))
    }

    @Test fun parsesClashYamlNodesAndIgnoresGroups() {
        val yaml = """proxies:
  - name: "clash-vless"
    type: vless
    server: edge.example
    port: 443
    uuid: synthetic-id
    tls: true
    servername: front.example
    network: ws
    ws-opts:
      path: /tunnel
      headers:
        Host: cdn.example
  - { name: "clash-ss", type: ss, server: ss.example, port: 8388, cipher: aes-128-gcm, password: synthetic-pass }
proxy-groups:
  - name: AUTO
    type: select
    proxies: [clash-vless, clash-ss]
"""
        val nodes = NodeShareParser.parse(yaml)
        assertEquals(listOf("clash-vless", "clash-ss"), nodes.map { it.name })
        assertEquals("vless", nodes.first().type)
        val first = org.json.JSONObject(nodes.first().raw)
        assertEquals("front.example", first.getJSONObject("tls").getString("server_name"))
        assertEquals("/tunnel", first.getJSONObject("transport").getString("path"))
        val second = org.json.JSONObject(nodes.last().raw)
        assertEquals("shadowsocks", second.getString("type"))
        assertEquals("aes-128-gcm", second.getString("method"))
    }

    @Test fun acceptsFullSingBoxJsonOutboundAndRejectsUnsupportedFormats() {
        val node = NodeShareParser.parse("""{"outbounds":[{"type":"hysteria2","tag":"hy2","server":"hy.example","server_port":443,"password":"synthetic-pass","tls":{"enabled":true}}]}""").single()
        assertEquals("hysteria2", node.type)
        assertEquals("synthetic-pass", org.json.JSONObject(node.raw).getString("password"))
        assertTrue(NodeShareParser.parse("""{"outbounds":[{"type":"mieru","server":"mi.example","server_port":51820}]}""").isEmpty())
    }

    @Test fun parsesLegacyShadowsocksServersAndRejectsHysteriaV1() {
        val ss = NodeShareParser.parse("""{"servers":[{"remarks":"legacy-ss","server":"ss.example","server_port":8388,"method":"aes-128-gcm","password":"synthetic-pass"}]}""").single()
        assertEquals("legacy-ss", ss.name)
        assertEquals("synthetic-pass", org.json.JSONObject(ss.raw).getString("password"))
        assertTrue(NodeShareParser.parse("""{"outbounds":[{"protocol":"hysteria","settings":{"version":1,"servers":[{"address":"hy.example","port":443,"password":"x"}]}}]}""").isEmpty())
    }

    @Test fun reportsUnsupportedOutboundTypesForClearImportFeedback() {
        assertEquals(listOf("mieru"), NodeShareParser.unsupportedTypes("""{"outbounds":[{"type":"mieru","server":"mi.example","server_port":51820}]}"""))
        assertEquals(emptyList<String>(), NodeShareParser.unsupportedTypes("""{"outbounds":[{"type":"direct","tag":"direct"},{"type":"block","tag":"block"},{"type":"vless","server":"v.example","server_port":443,"uuid":"synthetic"}]}"""))
    }

    @Test fun jsonConfigWithSingleProxyTakesRemarksAsName() {
        val json = """{"remarks":"🇺🇸 CebCeh-My","outbounds":[{"type":"vless","tag":"proxy","server":"edge.example","server_port":443,"uuid":"11111111-1111-1111-1111-111111111111"},{"type":"direct","tag":"direct"}]}"""
        val nodes = NodeShareParser.parse(json)
        assertEquals("🇺🇸 CebCeh-My", nodes.single().name)
        assertEquals("US", NodeCountry.resolve(nodes.single().name))
    }
}
