package ru.cebers.cehoproxy.core

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class StuckTunnelPolicyTest {
    @Test fun restartsOnlyAfterRequiredFailedChecks() {
        assertFalse(StuckTunnelPolicy.shouldRestart(StuckTunnelPolicy.FAILED_CHECKS_BEFORE_RESTART - 1, 1_000_000L, 0L))
        assertTrue(StuckTunnelPolicy.shouldRestart(StuckTunnelPolicy.FAILED_CHECKS_BEFORE_RESTART, 1_000_000L, 0L))
    }

    @Test fun waitsForCooldownBetweenAutomaticRestarts() {
        val last = 1_000_000L
        assertFalse(StuckTunnelPolicy.shouldRestart(5, last + StuckTunnelPolicy.MIN_RESTART_INTERVAL_MILLIS - 1, last))
        assertTrue(StuckTunnelPolicy.shouldRestart(5, last + StuckTunnelPolicy.MIN_RESTART_INTERVAL_MILLIS, last))
    }
}
