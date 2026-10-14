package ru.cebers.cehoproxy.vpn

import org.junit.Assert.*
import org.junit.Test
import java.util.concurrent.CountDownLatch

class BoundedCallTest {
    @Test fun returnsResultOfFastCall() {
        val call = BoundedCall("test-fast")
        try {
            assertEquals("ok", call.run(1000) { "ok" })
        } finally { call.shutdown() }
    }

    @Test fun givesUpOnHungCallAndNextCallStillRuns() {
        val call = BoundedCall("test-hung")
        val never = CountDownLatch(1)
        try {
            assertNull(call.run(100) { never.await(); "late" })
            assertEquals("next", call.run(1000) { "next" })
        } finally {
            never.countDown()
            call.shutdown()
        }
    }

    @Test fun failedCallGivesNull() {
        val call = BoundedCall("test-failed")
        try {
            assertNull(call.run(1000) { error("boom") })
        } finally { call.shutdown() }
    }
}
