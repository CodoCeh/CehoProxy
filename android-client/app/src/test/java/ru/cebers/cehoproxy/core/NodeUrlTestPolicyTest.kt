package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NodeUrlTestPolicyTest {
    @Test fun freshReplyUpdatesLatency() {
        val updated = NodeMetricsPolicy.applyUrlTest(NodeMeasurement(latencyMs = 90, measuredAt = 1_000L), 120, 2_000L)
        assertEquals(120, updated?.latencyMs)
        assertEquals(2_000L, updated?.measuredAt)
        assertTrue(updated?.failed == false)
    }

    @Test fun noReplyMarksNodeFailedInsteadOfKeepingOldLatency() {
        val updated = NodeMetricsPolicy.applyUrlTest(NodeMeasurement(latencyMs = 90, measuredAt = 1_000L), 0, 2_000L)
        assertNull(updated?.latencyMs)
        assertTrue(updated?.failed == true)
        assertEquals("Нет ответа", updated?.failureReason)
    }

    @Test fun olderUrlTestDoesNotOverwriteNewerManualCheck() {
        assertNull(NodeMetricsPolicy.applyUrlTest(NodeMeasurement(latencyMs = 80, measuredAt = 5_000L), 0, 4_000L))
    }

    @Test fun freshReplyEndsTheTestingState() {
        val updated = NodeMetricsPolicy.applyUrlTest(NodeMeasurement(testing = true, measuredAt = 1_000L), 150, 2_000L)
        assertTrue(updated?.testing == false)
        assertEquals(150, updated?.latencyMs)
    }
}
