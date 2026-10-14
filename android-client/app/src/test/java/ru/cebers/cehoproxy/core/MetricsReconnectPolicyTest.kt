package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class MetricsReconnectPolicyTest {
    @Test fun connectionFailureSchedulesRetryWithoutAnotherStartCall() {
        val policy = MetricsReconnectPolicy()
        policy.start()
        assertEquals(1_000L, policy.disconnected())
        assertNull(policy.disconnected())
        assertTrue(policy.retryReady())
        assertFalse(policy.retryReady())
        assertEquals(2_000L, policy.disconnected())
    }

    @Test fun repeatedFailuresBackOffWithBoundedDelayUntilDataArrives() {
        val policy = MetricsReconnectPolicy()
        policy.start()
        val delays = (0..6).map {
            val value = policy.disconnected()
            assertTrue(policy.retryReady())
            value
        }
        assertEquals(listOf(1_000L, 2_000L, 4_000L, 8_000L, 15_000L, 15_000L, 15_000L), delays)
        policy.receivedData()
        assertEquals(1_000L, policy.disconnected())
    }

    @Test fun backgroundOrVpnStopRejectsQueuedRetriesAndNewFailures() {
        val policy = MetricsReconnectPolicy()
        policy.start()
        assertNotNull(policy.disconnected())
        policy.stop()
        assertFalse(policy.active)
        assertFalse(policy.retryReady())
        assertNull(policy.disconnected())
        policy.start()
        assertEquals(1_000L, policy.disconnected())
    }
}
