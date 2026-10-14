package ru.cebers.cehoproxy.core

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SubscriptionExpiryTest {
    private val now = 1_800_000_000L
    private fun node(expiresAt: Long?) = ProxyNode("n", "vless", "s.example", 443, "vless://x", subscriptionExpiresAt = expiresAt)

    @Test fun allNodesExpiredInPast() = assertTrue(SubscriptionExpiry.allExpired(listOf(node(now - 1), node(now - 100)), now))

    @Test fun oneActiveNodeKeepsProfileUsable() = assertFalse(SubscriptionExpiry.allExpired(listOf(node(now - 1), node(now + 10)), now))

    @Test fun unknownExpiryCountsAsActive() = assertFalse(SubscriptionExpiry.allExpired(listOf(node(null)), now))

    @Test fun emptyListIsNotExpired() = assertFalse(SubscriptionExpiry.allExpired(emptyList(), now))
}
