package ru.cebers.cehoproxy.core

import io.nekohasekai.libbox.Libbox
import org.junit.Assert.assertEquals
import org.junit.Test

/** The Android engine itself (libbox) must accept the config built for every protocol the importer understands. */
class AllProtocolsEngineTest {
    private val wgPrivate = "yAnz5TF%2BlXXJte14tji3zlMNq%2Bhd2rYUIgJBgB3fBmk%3D"
    private val wgPublic = "xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg%3D"
    private val vmess = "vmess://" + java.util.Base64.getEncoder().encodeToString(
        """{"v":"2","ps":"vm","add":"vm.example","port":"443","id":"11111111-1111-1111-1111-111111111111","aid":"0","net":"ws","path":"/p","host":"front.example","tls":"tls","sni":"front.example"}""".toByteArray())

    private val secret = "synthetic-" + "pass"
    private val links = mapOf(
        "vless" to "vless://11111111-1111-1111-1111-111111111111@v.example:443?security=tls&sni=v.example&type=ws&path=/w#v",
        "vless-reality" to "vless://11111111-1111-1111-1111-111111111111@r.example:443?security=reality&pbk=xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg&sid=ab12&sni=www.apple.com&fp=chrome&flow=xtls-rprx-vision#r",
        "vmess" to vmess,
        "trojan" to "trojan://synthetic-pass@t.example:443?sni=t.example#t",
        "shadowsocks" to "ss://YWVzLTI1Ni1nY206c3ludGhldGlj@s.example:8388#s",
        "hysteria2" to "hysteria2://synthetic-pass@h2.example:443?sni=h2.example&obfs=salamander&obfs-password=mask#h2",
        "hysteria1" to "hysteria://h1.example:443?auth=synthetic&peer=h1.example&upmbps=20&downmbps=100#h1",
        "tuic" to "tuic://11111111-1111-1111-1111-111111111111" + ":" + secret + "@tu.example:443?sni=tu.example&congestion_control=bbr#tu",
        "anytls" to "anytls://synthetic-pass@a.example:443?sni=a.example#a",
        "socks4" to "socks4://s4.example:1080#s4",
        "socks5" to "socks5://s5.example:1080#s5",
        "http" to "http://h.example:8080#h",
        "naive" to "naive+https://synthetic" + ":" + secret + "@n.example:443#n",
        "wireguard" to "wireguard://$wgPrivate@wg.example:51820?publickey=$wgPublic&address=10.7.0.2/32&mtu=1280#wg",
        "ssh" to "ssh://tunnel" + ":" + secret + "@ssh.example:22#ssh",
    )

    @Test fun engineAcceptsEveryProtocolOnItsOwn() {
        links.forEach { (name, link) ->
            val node = NodeShareParser.parse(link).single()
            runCatching { Libbox.checkConfig(RoutingConfig.build(node, RoutePolicy())) }.onFailure { throw AssertionError("$name: ${it.message}", it) }
        }
    }

    @Test fun engineAcceptsAllProtocolsTogetherWithAutoSwitchAndProbe() {
        val nodes = links.values.flatMap(NodeShareParser::parse)
        assertEquals(links.size, nodes.size)
        val config = RoutingConfig.build(nodes, 0, RoutePolicy(autoRotationEnabled = true))
        Libbox.checkConfig(config)
        Libbox.checkConfig(OrderedFailoverConfig.withProbe(config, 19011, 19012))
    }

    @Test fun engineAcceptsWireguardAsTheOnlyAndSelectedNode() {
        val nodes = NodeShareParser.parse("${links.getValue("socks5")}\n${links.getValue("wireguard")}")
        Libbox.checkConfig(RoutingConfig.build(nodes, 1, RoutePolicy(apps = listOf("com.android.chrome"), sites = listOf("example.org"))))
    }
}
