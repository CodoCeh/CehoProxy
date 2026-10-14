package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class SubscriptionRefreshSelectionTest {
    private val url = "https://a.example/sub"
    private fun node(name: String, server: String, port: Int, raw: String) =
        ProxyNode(name, "vless", server, port, raw, subscriptionMetadata = SubscriptionMetadata(sourceHost = "a.example"))
    private val oldNodes = listOf(node("Ирландия-1", "ie.example", 443, "old-ie"), node("Албания-1", "al.example", 443, "old-al"))
    private val profile = ClientProfile(nodes = oldNodes, selected = 1, subscriptionUrls = listOf(url))

    @Test fun keepsSelectedNodeWhenProviderRotatesLink() {
        val fetched = mapOf(url to listOf(node("Ирландия-1", "ie.example", 443, "new-ie"), node("Албания-1", "al.example", 443, "new-al")))
        val result = SubscriptionRefreshPolicy.applyFetchedSources(profile, fetched, 1L)
        assertEquals("Албания-1", result.selectedNode?.name)
    }

    @Test fun matchesByAddressWhenNameChanges() {
        val fetched = mapOf(url to listOf(node("Ирландия-1", "ie.example", 443, "new-ie"), node("Албания-2", "al.example", 443, "new-al")))
        val result = SubscriptionRefreshPolicy.applyFetchedSources(profile, fetched, 1L)
        assertEquals("Албания-2", result.selectedNode?.name)
    }

    @Test fun fallsBackToFirstWhenSelectedNodeIsGone() {
        val fetched = mapOf(url to listOf(node("Ирландия-1", "ie.example", 443, "new-ie")))
        val result = SubscriptionRefreshPolicy.applyFetchedSources(profile, fetched, 1L)
        assertEquals(0, result.selected)
    }
}
