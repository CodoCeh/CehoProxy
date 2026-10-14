package ru.cebers.cehoproxy.core

internal class MetricsReconnectPolicy {
    var active: Boolean = false
        private set
    private var pending = false
    private var failures = 0

    fun start() { active = true; pending = false; failures = 0 }
    fun stop() { active = false; pending = false; failures = 0 }
    fun receivedData() { failures = 0 }

    fun disconnected(): Long? {
        if (!active || pending) return null
        pending = true
        val delay = (1_000L shl failures.coerceAtMost(4)).coerceAtMost(15_000L)
        failures = (failures + 1).coerceAtMost(4)
        return delay
    }

    fun retryReady(): Boolean {
        if (!active || !pending) return false
        pending = false
        return true
    }
}
