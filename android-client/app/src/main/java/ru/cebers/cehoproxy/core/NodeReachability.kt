package ru.cebers.cehoproxy.core

import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket

internal object NodeReachability {
    const val CONNECT_TIMEOUT_MILLIS = 3_000
    private val udpTypes = setOf("hysteria2", "tuic")

    /** TCP connect time to the node address. UDP-based nodes cannot be checked this way. */
    fun check(node: ProxyNode, nowMillis: Long): NodeMeasurement {
        if (node.type in udpTypes) return NodeMeasurement(failed = true, failureReason = "UDP: задержка без VPN не измеряется", measuredAt = nowMillis)
        return try {
            val started = System.nanoTime()
            Socket().use { it.connect(InetSocketAddress(node.server, node.port), CONNECT_TIMEOUT_MILLIS) }
            NodeMeasurement(latencyMs = ((System.nanoTime() - started) / 1_000_000).toInt(), measuredAt = nowMillis)
        } catch (_: IOException) {
            NodeMeasurement(failed = true, failureReason = "Нет ответа", measuredAt = nowMillis)
        } catch (_: IllegalArgumentException) {
            NodeMeasurement(failed = true, failureReason = "Неверный адрес", measuredAt = nowMillis)
        }
    }
}
