package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.ServerSocket

class NodeReachabilityTest {
    private fun node(type: String, port: Int) = ProxyNode("n", type, "127.0.0.1", port, "{}")

    @Test fun openTcpPortGivesLatency() {
        ServerSocket(0).use { server ->
            val result = NodeReachability.check(node("vless", server.localPort), nowMillis = 5_000L)
            assertNotNull(result.latencyMs)
            assertTrue(result.latencyMs!! >= 0)
            assertTrue(result.failed.not())
            assertEquals(5_000L, result.measuredAt)
        }
    }

    @Test fun closedPortIsReportedAsNoReply() {
        val port = ServerSocket(0).use { it.localPort }
        val result = NodeReachability.check(node("vless", port), nowMillis = 5_000L)
        assertNull(result.latencyMs)
        assertTrue(result.failed)
        assertEquals("Нет ответа: порт закрыт", result.failureReason)
    }

    @Test fun udpNodeIsNotMeasuredWithoutTunnel() {
        val result = NodeReachability.check(node("hysteria2", 443), nowMillis = 5_000L)
        assertNull(result.latencyMs)
        assertTrue(result.failed)
    }
}
