package ru.cebers.cehoproxy.vpn

import android.content.Context
import android.net.TrafficStats
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.os.Process
import android.os.SystemClock
import ru.cebers.cehoproxy.core.NodeMetricsPolicy

/** Tunnel traffic leaves through this app's sockets, so the app's own counters give the tunnel speed. Ticks only while the screen is on. */
internal class TrafficTicker(context: Context, private val publish: (String?) -> Unit) {
    private val power = context.getSystemService(PowerManager::class.java)
    private val handler = Handler(Looper.getMainLooper())
    @Volatile private var previous: Triple<Long, Long, Long>? = null
    @Volatile private var shown: String? = null
    @Volatile private var running = false

    private val tick = object : Runnable {
        override fun run() {
            if (!running) return
            sample()
            handler.postDelayed(this, INTERVAL_MILLIS)
        }
    }

    fun start() {
        if (running) return
        running = true
        previous = null
        handler.post(tick)
    }

    fun stop() {
        running = false
        handler.removeCallbacks(tick)
        previous = null
        shown = null
    }

    private fun sample() {
        val uid = Process.myUid()
        val rx = TrafficStats.getUidRxBytes(uid)
        val tx = TrafficStats.getUidTxBytes(uid)
        if (rx == TrafficStats.UNSUPPORTED.toLong() || tx == TrafficStats.UNSUPPORTED.toLong() || power?.isInteractive == false) { previous = null; return }
        val now = SystemClock.elapsedRealtimeNanos()
        val last = previous
        previous = Triple(rx, tx, now)
        if (last == null) return
        val text = "↓ ${NodeMetricsPolicy.speed(NodeMetricsPolicy.rate(last.first, rx, now - last.third) ?: 0)}  ↑ ${NodeMetricsPolicy.speed(NodeMetricsPolicy.rate(last.second, tx, now - last.third) ?: 0)} Мбит/с"
        if (text == shown) return
        shown = text
        publish(text)
    }

    private companion object { const val INTERVAL_MILLIS = 2_000L }
}
