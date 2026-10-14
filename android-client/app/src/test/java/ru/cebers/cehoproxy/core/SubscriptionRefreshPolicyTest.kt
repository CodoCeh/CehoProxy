package ru.cebers.cehoproxy.core

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SubscriptionRefreshPolicyTest {
    private val now = 1_800_000_000_000L
    private val hour = SubscriptionRefreshPolicy.HOUR_MILLIS

    @Test fun olderThanIntervalIsStale() = assertTrue(SubscriptionRefreshPolicy.isStale("https://sub.example/x", now - 4 * hour - 1, now, 4))

    @Test fun freshIsNotStale() = assertFalse(SubscriptionRefreshPolicy.isStale("https://sub.example/x", now - 1000, now, 4))

    @Test fun manualNodesWithoutUrlAreNeverStale() = assertFalse(SubscriptionRefreshPolicy.isStale(null, 0L, now, 4))

    @Test fun offFallsBackToDayOnConnect() {
        assertFalse(SubscriptionRefreshPolicy.isStale("https://sub.example/x", now - 13 * hour, now, 0))
        assertTrue(SubscriptionRefreshPolicy.isStale("https://sub.example/x", now - 25 * hour, now, 0))
    }
}
