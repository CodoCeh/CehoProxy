package ru.cebers.cehoproxy.core

import java.util.Base64
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SubscriptionFormatsTest {
    private val uuid = "11111111-1111-1111-1111-111111111111"
    private fun b64(text: String) = Base64.getEncoder().encodeToString(text.toByteArray())
    private fun b64Url(text: String) = Base64.getUrlEncoder().withoutPadding().encodeToString(text.toByteArray())

    private fun assertParsesAndBuilds(input: String, expectedTypes: List<String>): List<ProxyNode> {
        val nodes = NodeShareParser.parse(input)
        assertEquals("types of $expectedTypes", expectedTypes, nodes.map { it.type })
        nodes.forEach { node -> RoutingConfig.build(node, RoutePolicy()) }
        return nodes
    }

    @Test fun plainLinksWithCrlfCommentsAndBlankLines() {
        val text = "# comment line\r\n\r\nvless://$uuid@203.0.113.1:443?security=none#a\r\ntrojan://pass@203.0.113.2:443?security=tls&sni=example.com#b\r\n"
        assertParsesAndBuilds(text, listOf("vless", "trojan"))
    }

    @Test fun base64ListStandardAndUrlSafeWithoutPadding() {
        val list = "vless://$uuid@203.0.113.1:443?security=none#a\nhysteria2://pass@203.0.113.3:443?sni=example.com#c\n"
        assertParsesAndBuilds(b64(list), listOf("vless", "hysteria2"))
        assertParsesAndBuilds(b64Url(list), listOf("vless", "hysteria2"))
    }

    @Test fun shadowsocksSip002AndLegacyForms() {
        val sip002 = "ss://${b64Url("aes-256-gcm:pass")}@203.0.113.11:8388#ss1"
        val legacy = "ss://${b64Url("aes-256-gcm:pass@203.0.113.12:8388")}#ss2"
        assertParsesAndBuilds(sip002 + "\n" + legacy, listOf("ss", "ss"))
    }

    @Test fun vmessBase64Json() {
        val json = """{"v":"2","ps":"vm","add":"203.0.113.10","port":"443","id":"$uuid","aid":"0","net":"ws","type":"none","host":"example.com","path":"/ws","tls":"tls","sni":"example.com"}"""
        assertParsesAndBuilds("vmess://${b64(json)}", listOf("vmess"))
    }

    @Test fun trojanTuicHysteria2AndVlessVariants() {
        val text = listOf(
            "trojan://pass@203.0.113.2:443?security=tls&sni=example.com&type=ws&path=%2Fws#tr",
            "hy2://pass@203.0.113.14:443?sni=example.com&insecure=1#hy",
            "tuic://$uuid:pass@203.0.113.15:443?sni=example.com&congestion_control=bbr#tu",
            "vless://$uuid@203.0.113.16:443?encryption=none&security=reality&sni=example.com&fp=chrome&pbk=AAAA&sid=ab&type=grpc&serviceName=svc&flow=xtls-rprx-vision#vg",
            "vless://$uuid@203.0.113.17:443?security=tls&sni=example.com&type=ws&path=%2Fws&host=example.com#vw",
            "vless://$uuid@[2001:db8::1]:443?security=none#v6",
        ).joinToString("\n")
        assertParsesAndBuilds(text, listOf("trojan", "hysteria2", "tuic", "vless", "vless", "vless"))
    }

    @Test fun xrayJsonArrayAndSingObject() {
        val xray = """[{"remarks":"x1","outbounds":[{"protocol":"vless","settings":{"vnext":[{"address":"203.0.113.20","port":443,"users":[{"id":"$uuid","encryption":"none","flow":"xtls-rprx-vision"}]}]},"streamSettings":{"network":"tcp","security":"reality","realitySettings":{"serverName":"example.com","publicKey":"AAAA","shortId":"ab","fingerprint":"chrome"}},"tag":"proxy"},{"protocol":"freedom","tag":"direct"}]}]"""
        assertParsesAndBuilds(xray, listOf("vless"))
        val singBox = """{"outbounds":[{"type":"selector","tag":"s","outbounds":["a"]},{"type":"vless","tag":"a","server":"203.0.113.40","server_port":443,"uuid":"$uuid","tls":{"enabled":true,"server_name":"example.com","reality":{"enabled":true,"public_key":"AAAA","short_id":"ab"}}},{"type":"direct","tag":"direct"}]}"""
        assertParsesAndBuilds(singBox, listOf("vless"))
    }

    @Test fun clashYamlPlainAndBase64() {
        val yaml = """
            proxies:
              - name: "c1"
                type: vmess
                server: 203.0.113.30
                port: 443
                uuid: $uuid
                alterId: 0
                cipher: auto
                tls: true
                network: ws
              - {name: c2, type: trojan, server: 203.0.113.31, port: 443, password: pass, sni: example.com}
              - {name: c3, type: ss, server: 203.0.113.32, port: 8388, cipher: aes-256-gcm, password: pass}
              - {name: c4, type: hysteria2, server: 203.0.113.33, port: 443, password: pass, sni: example.com}
              - {name: c5, type: vless, server: 203.0.113.34, port: 443, uuid: $uuid, tls: true, servername: example.com, reality-opts: {public-key: AAAA, short-id: ab}, client-fingerprint: chrome, network: tcp, flow: xtls-rprx-vision}
            proxy-groups:
              - {name: auto, type: url-test, proxies: [c1]}
        """.trimIndent()
        assertParsesAndBuilds(yaml, listOf("vmess", "trojan", "shadowsocks", "hysteria2", "vless"))
        assertParsesAndBuilds(b64(yaml), listOf("vmess", "trojan", "shadowsocks", "hysteria2", "vless"))
    }

    @Test fun happLevelLinkVariantsParseAndBuildEachOnItsOwn() {
        val key16 = Base64.getEncoder().encodeToString(ByteArray(16) { it.toByte() })
        fun vmessLink(extra: String) = "vmess://" + b64("""{"v":"2","ps":"vm","add":"203.0.113.80","port":"443","id":"$uuid","aid":"0","tls":"tls","sni":"example.com",$extra}""")
        val links = listOf(
            "hysteria2://pass@203.0.113.60:443?sni=example.com&obfs=salamander&obfs-password=secret&mport=20000-20010#hy-obfs",
            "vless://$uuid@203.0.113.61:443?encryption=none&security=tls&sni=example.com&type=httpupgrade&host=example.com&path=%2Fhu#hu",
            "vless://$uuid@203.0.113.62:443?encryption=none&security=tls&sni=example.com&type=h2&host=example.com&path=%2Fh2#h2",
            "vless://$uuid@203.0.113.63:443?security=reality&sni=example.com&fp=chrome&pbk=AAAA&sid=ab&spx=%2F&type=tcp&flow=xtls-rprx-vision#reality-spx",
            "vless://$uuid@203.0.113.64:443?security=tls&sni=example.com&alpn=h2,http/1.1&allowInsecure=1&type=tcp#alpn",
            vmessLink(""""net":"h2","path":"/h2""""),
            vmessLink(""""net":"grpc","path":"svc""""),
            vmessLink(""""net":"httpupgrade","path":"/hu""""),
            "trojan://pass@203.0.113.65:443?security=tls&sni=example.com&type=grpc&serviceName=svc#trgrpc",
            "ss://${b64Url("2022-blake3-aes-128-gcm:$key16")}@203.0.113.66:8388#ss2022",
            "tuic://$uuid:pass@203.0.113.68:443?sni=example.com&alpn=h3&congestion_control=bbr#tuic-alpn",
            "socks://${b64Url("user:pass")}@203.0.113.69:1080#socks",
            "http://user:pass@203.0.113.70:8080#http",
            "vless://$uuid@203.0.113.71:443?security=tls&sni=example.com&type=ws&path=%2Fws&host=example.com&ed=2048#ws-ed",
        )
        val failures = links.mapNotNull { link ->
            runCatching {
                val nodes = NodeShareParser.parse(link)
                check(nodes.size == 1) { "parsed ${nodes.size}" }
                RoutingConfig.build(nodes.single(), RoutePolicy())
                null
            }.getOrElse { "${link.substringBefore("://")}://…: ${it.message}" }
        }
        assertEquals(emptyList<String>(), failures)
    }

    @Test fun appliedFieldsReachOutboundConfig() {
        val legacy = NodeShareParser.parse("ss://${b64Url("aes-256-gcm:pass@203.0.113.12:8388")}#ss2").single()
        val legacyOut = JSONObject(RoutingConfig.build(legacy, RoutePolicy())).getJSONArray("outbounds").getJSONObject(0)
        assertEquals("aes-256-gcm", legacyOut.getString("method"))
        assertEquals("pass", legacyOut.getString("password"))
        val plain = NodeShareParser.parse("ss://aes-128-gcm:p%40ss@203.0.113.13:8388#ssplain").single()
        val plainOut = JSONObject(RoutingConfig.build(plain, RoutePolicy())).getJSONArray("outbounds").getJSONObject(0)
        assertEquals("aes-128-gcm", plainOut.getString("method"))
        assertEquals("p@ss", plainOut.getString("password"))
        val hop = NodeShareParser.parse("hysteria2://pass@203.0.113.60:443?sni=example.com&mport=20000-20010,30000#hop").single()
        val hopOut = JSONObject(RoutingConfig.build(hop, RoutePolicy())).getJSONArray("outbounds").getJSONObject(0)
        assertEquals(listOf("20000:20010", "30000"), hopOut.getJSONArray("server_ports").let { a -> (0 until a.length()).map { a.getString(it) } })
    }

    @Test fun shadowsocksWithPluginIsRejectedInsteadOfConnectingWithoutIt() {
        val node = NodeShareParser.parse("ss://${b64Url("aes-256-gcm:pass")}@203.0.113.67:8388?plugin=obfs-local%3Bobfs%3Dhttp#ssplugin").single()
        val error = runCatching { RoutingConfig.build(node, RoutePolicy()) }.exceptionOrNull()
        assertTrue(error is IllegalArgumentException)
        assertEquals("SS-плагин не поддерживается на Android", error?.message)
    }

    @Test fun xhttpNodeParsesAlongsideOtherNodes() {
        val text = "vless://$uuid@203.0.113.50:443?type=xhttp&security=none#xh\nvless://$uuid@203.0.113.51:443?security=none#ok"
        val nodes = NodeShareParser.parse(text)
        assertEquals(2, nodes.size)
        assertTrue(nodes.any { it.name == "ok" })
    }
}
