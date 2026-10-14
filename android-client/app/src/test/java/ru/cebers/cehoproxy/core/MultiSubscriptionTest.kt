package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class MultiSubscriptionTest {
    private fun node(name: String, uuid: String, host: String?): ProxyNode =
        NodeShareParser.parse("vless://$uuid@$uuid.example:443#$name").single().copy(subscriptionMetadata = SubscriptionMetadata(sourceHost = host))

    @Test fun refreshingOneSourceKeepsOtherSourcesNodes() {
        val first = node("A-1", "11111111-1111-1111-1111-111111111111", "one.example")
        val second = node("B-1", "22222222-2222-2222-2222-222222222222", "two.example")
        val profile = ClientProfile(nodes = listOf(first, second), subscriptionUrls = listOf("https://one.example/sub", "https://two.example/sub"))
        val freshFirst = node("A-2", "33333333-3333-3333-3333-333333333333", "one.example")
        val updated = SubscriptionRefreshPolicy.applyFetchedSources(profile, mapOf("https://one.example/sub" to listOf(freshFirst)), 42L)
        assertEquals(listOf(second.raw, freshFirst.raw), updated.nodes.map { it.raw })
        assertEquals(42L, updated.subscriptionFetchedAt)
    }

    @Test fun profileKeepsSeveralSubscriptionUrlsAndRootIsFirst() {
        val profile = ProfileCodec.decode(ProfileCodec.encode(ClientProfile(subscriptionUrls = listOf("https://one.example/sub", "https://two.example/sub"))))
        assertEquals(listOf("https://one.example/sub", "https://two.example/sub"), profile.subscriptionUrls)
        assertEquals("https://one.example/sub", profile.subscriptionUrl)
    }
}
