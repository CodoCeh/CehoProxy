package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SubscriptionShareTest {
    private fun node(name: String, uuid: String, url: String?): ProxyNode =
        NodeShareParser.parse("vless://$uuid@$name.example:443?security=tls#$name").single()
            .copy(subscriptionMetadata = SubscriptionMetadata(sourceUrl = url, sourceHost = url?.let { SubscriptionRemovalPolicy.hostOf(it) }, title = url?.let { "Подписка ${SubscriptionRemovalPolicy.hostOf(it)}" }))

    private val first = "https://one.example/sub/a"
    private val second = "https://two.example/sub/b"
    private val profile = ClientProfile(
        subscriptionUrls = listOf(first, second),
        nodes = listOf(
            node("a1", "11111111-1111-1111-1111-111111111111", first),
            node("a2", "22222222-2222-2222-2222-222222222222", first),
            node("b1", "33333333-3333-3333-3333-333333333333", second),
            node("own", "44444444-4444-4444-4444-444444444444", null),
        ),
    )

    @Test fun groupsBySubscriptionAndKeepsOwnServersApart() {
        val groups = SubscriptionShare.groups(profile)
        assertEquals(listOf(first, second, SubscriptionShare.OWN_SERVERS), groups.map { it.key })
        assertEquals(listOf(2, 1, 1), groups.map { it.nodes.size })
    }

    @Test fun sendsOnlyTheChosenSubscription() {
        val incoming = SubscriptionShare.decode(SubscriptionShare.encode(profile, setOf(second)))!!
        assertEquals(listOf(second), incoming.urls)
        assertEquals(listOf("b1"), incoming.nodes.map { it.name })
        assertEquals("Подписка two.example", incoming.nodes.single().subscriptionMetadata.title)
    }

    @Test fun receiverAddsWithoutLosingWhatItHad() {
        val receiver = ClientProfile(subscriptionUrls = listOf(first), nodes = listOf(profile.nodes[0]))
        val incoming = SubscriptionShare.decode(SubscriptionShare.encode(profile, setOf(first, SubscriptionShare.OWN_SERVERS)))!!
        val merged = SubscriptionShare.merge(receiver, incoming)
        assertEquals(listOf(first), merged.subscriptionUrls)
        assertEquals(listOf("a1", "a2", "own"), merged.nodes.map { it.name })
    }

    @Test fun readsTheOlderWholeProfileBackup() {
        val incoming = SubscriptionShare.decode(ProfileBackup.encode(profile, ProfileExportScope.SUBSCRIPTIONS))!!
        assertEquals(4, incoming.nodes.size)
        assertEquals(listOf(first, second), incoming.urls)
    }

    @Test fun rejectsOtherText() {
        assertNull(SubscriptionShare.decode("https://example.com/sub"))
        assertNull(SubscriptionShare.decode("{\"format\":\"other\"}"))
        assertTrue(SubscriptionShare.summary(SubscriptionShare.decode(SubscriptionShare.encode(profile, setOf(first)))!!).single().second == 2)
    }
}
