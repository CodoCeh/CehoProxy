package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class SameHostSubscriptionsTest {
    private val first = "https://hy2-sub.example/sub/one"
    private val second = "https://hy2-sub.example/sub/two"
    private fun node(raw: String, url: String?) = ProxyNode("proxy", "hysteria2", "origin.example", 8443, raw,
        subscriptionMetadata = SubscriptionMetadata(sourceHost = "hy2-sub.example", sourceUrl = url))
    private val tagged = ClientProfile(
        nodes = listOf(node("a1", first), node("b1", second)),
        subscriptionUrls = listOf(first, second),
    )

    @Test fun refreshOneSubscriptionKeepsTheOtherOnSameHost() {
        val result = SubscriptionRefreshPolicy.applyFetchedSources(tagged, mapOf(first to listOf(node("a2", first))), 1L)
        assertEquals(listOf("b1", "a2"), result.nodes.map { it.raw })
    }

    @Test fun removingOneSubscriptionKeepsTheOtherOnSameHost() {
        val result = SubscriptionRemovalPolicy.remove(tagged, first)
        assertEquals(listOf("b1"), result.nodes.map { it.raw })
        assertEquals(listOf(second), result.subscriptionUrls)
    }

    @Test fun sameConfigFromTwoSubscriptionsStaysInBoth() {
        val result = SubscriptionRefreshPolicy.applyFetchedSources(tagged, mapOf(first to listOf(node("b1", first))), 1L)
        assertEquals(listOf("b1", "b1"), result.nodes.map { it.raw })
        assertEquals(listOf(second, first), result.nodes.map { it.subscriptionMetadata.sourceUrl })
    }

    @Test fun urlForPrefersExactUrl() {
        assertEquals(second, SubscriptionRemovalPolicy.urlFor(tagged, SubscriptionMetadata(sourceHost = "hy2-sub.example", sourceUrl = second)))
    }

    @Test fun sourceUrlSurvivesProfileRoundTrip() {
        val restored = ProfileCodec.decode(ProfileCodec.encode(tagged))
        assertEquals(second, restored.nodes[1].subscriptionMetadata.sourceUrl)
    }
}
