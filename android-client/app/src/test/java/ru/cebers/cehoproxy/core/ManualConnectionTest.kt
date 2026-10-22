package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ManualConnectionTest {
    private fun parsed(input: ManualConnection) = NodeShareParser.parse(ManualConnectionPolicy.toUri(input)).single()

    @Test fun httpsProxyNeedsLoginAndPassword() {
        val base = ManualConnection(ManualProtocol.HTTPS, host = "proxy.example", username = "u", password = "p")
        assertNull(ManualConnectionPolicy.validate(base))
        assertNotNull(ManualConnectionPolicy.validate(base.copy(username = "")))
        assertNotNull(ManualConnectionPolicy.validate(base.copy(password = "")))
    }

    @Test fun socksAndHttpWorkWithoutLogin() {
        listOf(ManualProtocol.SOCKS5, ManualProtocol.HTTP).forEach { protocol ->
            assertNull(ManualConnectionPolicy.validate(ManualConnection(protocol, host = "10.0.0.5")))
        }
        assertNotNull(ManualConnectionPolicy.validate(ManualConnection(ManualProtocol.SOCKS5, host = "10.0.0.5", password = "secret")))
    }

    @Test fun addressAndPortAreChecked() {
        assertNotNull(ManualConnectionPolicy.validate(ManualConnection(host = "")))
        assertNotNull(ManualConnectionPolicy.validate(ManualConnection(host = "https://proxy.example", username = "u", password = "p")))
        assertNotNull(ManualConnectionPolicy.validate(ManualConnection(host = "proxy.example/path", username = "u", password = "p")))
        assertNotNull(ManualConnectionPolicy.validate(ManualConnection(host = "proxy.example", port = "0", username = "u", password = "p")))
        assertNotNull(ManualConnectionPolicy.validate(ManualConnection(host = "proxy.example", port = "70000", username = "u", password = "p")))
        assertNotNull(ManualConnectionPolicy.validate(ManualConnection(host = "proxy.example", port = "80a", username = "u", password = "p")))
    }

    @Test fun emptyPortFallsBackToProtocolDefault() {
        assertEquals(8443, parsed(ManualConnection(ManualProtocol.HTTPS, host = "proxy.example", username = "u", password = "p")).port)
        assertEquals(1080, parsed(ManualConnection(ManualProtocol.SOCKS5, host = "10.0.0.5")).port)
        assertEquals(8080, parsed(ManualConnection(ManualProtocol.HTTP, host = "10.0.0.5")).port)
    }

    @Test fun linkRoundTripsThroughTheOrdinaryParser() {
        val node = parsed(ManualConnection(ManualProtocol.HTTPS, host = "proxy.example", port = "9443", username = "user", password = "pw", name = "Офис"))
        assertEquals("naive", node.type)
        assertEquals("proxy.example", node.server)
        assertEquals(9443, node.port)
        assertEquals("Офис", node.name)
        assertEquals("socks5", parsed(ManualConnection(ManualProtocol.SOCKS5, host = "10.0.0.5", port = "1081")).type)
        assertEquals("http", parsed(ManualConnection(ManualProtocol.HTTP, host = "10.0.0.5", port = "3128", username = "a", password = "b")).type)
    }

    @Test fun specialCharactersInLoginAndPasswordAreEncodedAndRestored() {
        val password = "p@ss:w/rd#?%+ я"
        val uri = ManualConnectionPolicy.toUri(ManualConnection(ManualProtocol.SOCKS5, host = "10.0.0.5", username = "dom\\user@corp", password = password))
        val parsedUri = java.net.URI(uri)
        assertEquals("10.0.0.5", parsedUri.host)
        assertEquals(1080, parsedUri.port)
        val (login, pass) = parsedUri.rawUserInfo.split(':').map { java.net.URLDecoder.decode(it.replace("+", "%2B"), "UTF-8") }
        assertEquals("dom\\user@corp", login)
        assertEquals(password, pass)
        assertTrue(NodeShareParser.parse(uri).size == 1)
    }

    @Test fun tlsOptionsOnlyForHttpsProxy() {
        val https = ManualConnectionPolicy.toUri(ManualConnection(ManualProtocol.HTTPS, host = "1.2.3.4", username = "u", password = "p", serverName = "corp.example"))
        assertTrue(https, "sni=corp.example" in https && "insecure" !in https)
        val socks = ManualConnectionPolicy.toUri(ManualConnection(ManualProtocol.SOCKS5, host = "1.2.3.4", serverName = "corp.example"))
        assertTrue(socks, "sni" !in socks && "insecure" !in socks)
    }

    @Test fun ipv6AddressIsBracketed() {
        val node = parsed(ManualConnection(ManualProtocol.SOCKS5, host = "2001:db8::1", port = "1080"))
        assertEquals(1080, node.port)
    }

    @Test fun removingTheSelectedNodeKeepsProfileConsistent() {
        fun n(name: String) = ProxyNode(name, "socks", "h", 1, "socks://h:1#$name")
        val a = n("a"); val b = n("b"); val c = n("c")
        val profile = ClientProfile(nodes = listOf(a, b, c), selected = 1, appNodeRoutes = mapOf("x" to b.routeId, "y" to c.routeId), siteNodeRoutes = mapOf("s" to b.routeId), autoExcludedRouteIds = listOf(b.routeId, c.routeId))
        val next = NodeRemovalPolicy.remove(profile, b.raw)
        assertEquals(listOf("a", "c"), next.nodes.map { it.name })
        assertEquals(1, next.selected)
        assertEquals(mapOf("y" to c.routeId), next.appNodeRoutes)
        assertTrue(next.siteNodeRoutes.isEmpty())
        assertEquals(listOf(c.routeId), next.autoExcludedRouteIds)
        assertEquals("c", NodeRemovalPolicy.remove(profile.copy(selected = 2), b.raw).selectedNode?.name)
        assertEquals(0, NodeRemovalPolicy.remove(profile.copy(nodes = listOf(a), selected = 0), a.raw).selected)
    }

    @Test fun onlySubscriptionlessNodesCanBeRemovedOneByOne() {
        val manual = ProxyNode("m", "socks", "h", 1, "socks://h:1#m")
        val fromSubscription = ProxyNode("s", "socks", "h", 2, "socks://h:2#s", subscriptionMetadata = SubscriptionMetadata(sourceHost = "sub.example", sourceUrl = "https://sub.example/a"))
        val profile = ClientProfile(nodes = listOf(manual, fromSubscription), subscriptionUrls = listOf("https://sub.example/a"))
        assertTrue(NodeRemovalPolicy.removable(profile, manual))
        assertTrue(!NodeRemovalPolicy.removable(profile, fromSubscription))
    }
}
