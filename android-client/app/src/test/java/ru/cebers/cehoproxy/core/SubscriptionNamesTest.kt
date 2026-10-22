package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SubscriptionNamesTest {
    private fun node(name: String, server: String, port: Int) = ProxyNode(name, "hysteria2", server, port, "{}")

    @Test fun readsTagsFromNamedXrayContainerAndSingBoxOutbound() {
        val xray = """[{"remarks":"x","outbounds":[{"tag":"🇺🇸 Name-A","protocol":"hysteria","settings":{"version":2,"address":"a.example","port":8443}},{"tag":"direct","protocol":"freedom"}]}]"""
        assertEquals(mapOf("a.example:8443" to "🇺🇸 Name-A"), SubscriptionNames.fromBody(xray))
        val singbox = """{"outbounds":[{"type":"hysteria2","tag":"Name-B","server":"b.example","server_port":443}]}"""
        assertEquals(mapOf("b.example:443" to "Name-B"), SubscriptionNames.fromBody(singbox))
    }

    @Test fun renamesOnlyGenericNodesMatchedByServerAndPort() {
        val nodes = listOf(node("proxy", "a.example", 8443), node("proxy", "c.example", 443), node("Kept", "a.example", 8443))
        val renamed = SubscriptionNames.apply(nodes, mapOf("a.example:8443" to "🇺🇸 Name-A"))
        assertEquals(listOf("🇺🇸 Name-A", "proxy", "Kept"), renamed.map { it.name })
        assertEquals(nodes.map { it.raw }, renamed.map { it.raw })
    }

    @Test fun addressWithSeveralTagsIsNotNamed() {
        val xray = """[{"outbounds":[{"tag":"Германия","protocol":"hysteria","settings":{"version":2,"address":"o.example","port":8443}}]},{"outbounds":[{"tag":"Швеция","protocol":"hysteria","settings":{"version":2,"address":"o.example","port":8443}}]}]"""
        assertEquals(emptyMap<String, String>(), SubscriptionNames.fromBody(xray))
        val nodes = listOf(node("proxy", "o.example", 8443))
        assertEquals(listOf("proxy"), SubscriptionNames.apply(nodes, SubscriptionNames.fromBody(xray)).map { it.name })
    }

    @Test fun genericNameDetection() {
        assertTrue(SubscriptionNames.isGeneric("proxy"))
        assertTrue(SubscriptionNames.isGeneric(" Proxy "))
        assertFalse(SubscriptionNames.isGeneric("Германия-4"))
    }

    private val native = """{"outbounds":[{"type":"hysteria2","tag":"proxy","server":"o.example","server_port":8443,"password":"own"},{"type":"direct","tag":"direct"}]}"""
    private val named = """[{"outbounds":[{"tag":"Германия","protocol":"hysteria","settings":{"version":2,"address":"o.example","port":8443,"password":"a"}}]},{"outbounds":[{"tag":"Швеция","protocol":"hysteria","settings":{"version":2,"address":"o.example","port":8443,"password":"b"}}]}]"""

    @Test fun namedListKeepsTheNativeNodeWithItsOwnAccount() {
        val result = SubscriptionNames.resolve("https://sub.example/", NodeShareParser.parse(native)) { _, _ -> named }
        assertEquals(listOf("Германия", "Швеция", "proxy"), result.map { it.name })
    }

    @Test fun failedNamesRequestKeepsNativeNodesOnFirstImport() {
        val result = SubscriptionNames.resolve("https://sub.example/", NodeShareParser.parse(native)) { _, _ -> throw java.io.IOException("timeout") }
        assertEquals(listOf("proxy"), result.map { it.name })
    }

    @Test fun failedNamesRequestFailsTheRefreshSoSavedNodesStay() {
        val failure = runCatching { SubscriptionNames.resolve("https://sub.example/", NodeShareParser.parse(native), required = true) { _, _ -> throw java.io.IOException("timeout") } }
        assertTrue(failure.exceptionOrNull() is java.io.IOException)
    }
}
