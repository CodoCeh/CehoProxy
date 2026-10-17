package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class FailoverHealthPolicyTest {
    @Test fun singleFailedRoundDoesNotShowError() {
        val failed = FailoverHealthPolicy.nextFailedRounds(0, healthy = false, sinceStartMillis = 120_000L)
        assertEquals(1, failed)
        assertFalse(FailoverHealthPolicy.showsError(failed))
    }

    @Test fun errorAppearsAfterRequiredConsecutiveFailedRounds() {
        assertTrue(FailoverHealthPolicy.showsError(FailoverHealthPolicy.FAILED_ROUNDS_BEFORE_ERROR))
        assertFalse(FailoverHealthPolicy.showsError(FailoverHealthPolicy.FAILED_ROUNDS_BEFORE_ERROR - 1))
    }

    @Test fun healthyRoundResetsTheCounter() {
        assertEquals(0, FailoverHealthPolicy.nextFailedRounds(5, healthy = true, sinceStartMillis = 120_000L))
    }

    @Test fun failuresDuringStartupGraceAreNotCounted() {
        val graceEnd = FailoverHealthPolicy.STARTUP_GRACE_MILLIS
        assertEquals(0, FailoverHealthPolicy.nextFailedRounds(0, healthy = false, sinceStartMillis = 0L))
        assertEquals(0, FailoverHealthPolicy.nextFailedRounds(0, healthy = false, sinceStartMillis = graceEnd - 1))
        assertEquals(1, FailoverHealthPolicy.nextFailedRounds(0, healthy = false, sinceStartMillis = graceEnd))
    }

    @Test fun failuresKeepCountingAfterGrace() {
        var failed = 0
        repeat(FailoverHealthPolicy.FAILED_ROUNDS_BEFORE_ERROR) {
            failed = FailoverHealthPolicy.nextFailedRounds(failed, healthy = false, sinceStartMillis = 300_000L)
        }
        assertTrue(FailoverHealthPolicy.showsError(failed))
    }
}
