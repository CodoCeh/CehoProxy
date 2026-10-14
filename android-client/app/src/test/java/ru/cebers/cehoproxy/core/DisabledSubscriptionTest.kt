package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class DisabledSubscriptionTest {
    private fun node(name: String, uuid: String, host: String?): ProxyNode =
        NodeShareParser.parse("vless://$uuid@$uuid.example:443#$name").single().copy(subscriptionMetadata = SubscriptionMetadata(sourceHost = host))

    private val root = node("Россия-1", "11111111-1111-1111-1111-111111111111", "sub.cebceh.com")
    private val other = node("Франция-1", "22222222-2222-2222-2222-222222222222", "other.example")

    @Test fun disabledSubscriptionNodesLeaveRoutingConfig() {
        val config = RoutingConfig.build(listOf(root, other), 0, RoutePolicy(disabledSubscriptions = setOf("sub.cebceh.com")))
        assertTrue(!config.contains("11111111-1111"))
        assertTrue(config.contains("22222222-2222"))
    }

    @Test fun disabledSubscriptionIsNotReplacedByFallback() {
        assertTrue(RoutingConfig.allowedNodesFor(listOf(root), emptySet(), setOf("sub.cebceh.com")).isEmpty())
    }

    @Test fun subscriptionKeyUsesHostAndProfileRoundTrips() {
        assertEquals("sub.cebceh.com", NodePresentation.subscriptionKey(root))
        val profile = ProfileCodec.decode(ProfileCodec.encode(ClientProfile(disabledSubscriptions = listOf("sub.cebceh.com"))))
        assertEquals(listOf("sub.cebceh.com"), profile.disabledSubscriptions)
    }
}
