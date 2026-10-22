package ru.cebers.cehoproxy.vpn

import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.Callable
import java.util.concurrent.Executors

/** Tells a dead network from a carrier that lets only Russian sites through, so the user is told the real reason. */
internal object DirectReachability {
    enum class State { OPEN, RESTRICTED, OFFLINE }

    private const val TIMEOUT_MILLIS = 2_500
    private val foreign = listOf("1.1.1.1" to 443, "8.8.8.8" to 443)
    private val domestic = listOf("ya.ru" to 443, "77.88.8.8" to 443)

    fun classify(foreignReachable: Boolean, domesticReachable: Boolean): State = when {
        foreignReachable -> State.OPEN
        domesticReachable -> State.RESTRICTED
        else -> State.OFFLINE
    }

    /** Connects without the tunnel: this app is excluded from the VPN, so these sockets use the physical network. */
    fun check(): State {
        val pool = Executors.newFixedThreadPool(foreign.size + domestic.size) { task -> Thread(task, "ceho-direct").apply { isDaemon = true } }
        return try {
            val foreignResults = foreign.map { (host, port) -> pool.submit(Callable { reaches(host, port) }) }
            val domesticResults = domestic.map { (host, port) -> pool.submit(Callable { reaches(host, port) }) }
            classify(foreignResults.any { it.get() }, domesticResults.any { it.get() })
        } finally { pool.shutdownNow() }
    }

    private fun reaches(host: String, port: Int): Boolean = try {
        Socket().use { it.connect(InetSocketAddress(host, port), TIMEOUT_MILLIS) }
        true
    } catch (_: Exception) { false }
}
