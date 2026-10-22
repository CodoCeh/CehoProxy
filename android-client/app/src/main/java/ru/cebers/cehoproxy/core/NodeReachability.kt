package ru.cebers.cehoproxy.core

import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket

internal object NodeReachability {
    const val CONNECT_TIMEOUT_MILLIS = 3_000
    private val udpTypes = setOf("hysteria2", "tuic")

    fun describe(error: IOException): String = when (error) {
        is java.net.SocketTimeoutException -> "Нет ответа: таймаут"
        is java.net.UnknownHostException -> "Нет ответа: адрес не найден"
        is java.net.ConnectException -> "Нет ответа: порт закрыт"
        else -> "Нет ответа"
    }

    fun isUdp(node: ProxyNode): Boolean = node.type in udpTypes

    /** TCP connect time to the node address. UDP-based nodes cannot be checked this way. */
    fun check(node: ProxyNode, nowMillis: Long): NodeMeasurement {
        if (node.type in udpTypes) return NodeMeasurement(failed = true, failureReason = "UDP: задержка без VPN не измеряется", measuredAt = nowMillis)
        return try {
            val started = System.nanoTime()
            Socket().use { it.connect(InetSocketAddress(node.server, node.port), CONNECT_TIMEOUT_MILLIS) }
            NodeMeasurement(latencyMs = ((System.nanoTime() - started) / 1_000_000).toInt(), measuredAt = nowMillis)
        } catch (error: IOException) {
            NodeMeasurement(failed = true, failureReason = describe(error), measuredAt = nowMillis)
        } catch (_: IllegalArgumentException) {
            NodeMeasurement(failed = true, failureReason = "Неверный адрес", measuredAt = nowMillis)
        }
    }
}
