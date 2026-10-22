package ru.cebers.cehoproxy.vpn

import io.nekohasekai.libbox.*
import java.util.concurrent.ConcurrentHashMap

/** Runs the engine's parallel URL test and reports which nodes answered it, so dead nodes are not probed one by one. */
internal class LatencyScout(private val groupTag: String) {
    /** Returns the answering members of [expected], or null when the test could not run. */
    fun alive(expected: Set<String>, waitMillis: Long): Set<String>? {
        val startedAt = System.currentTimeMillis()
        val answered = ConcurrentHashMap.newKeySet<String>()
        val options = CommandClientOptions().apply {
            statusInterval = 1_000_000_000L
            addCommand(Libbox.CommandOutbounds)
        }
        val client = Libbox.newCommandClient(object : CommandClientHandler {
            override fun writeOutbounds(items: OutboundGroupItemIterator) {
                while (items.hasNext()) {
                    val item = items.next()
                    if (item.urlTestDelay > 0 && item.urlTestTime * 1000 >= startedAt - 1000) answered.add(item.tag)
                }
            }
            override fun connected() {}
            override fun disconnected(message: String?) {}
            override fun writeStatus(status: StatusMessage) {}
            override fun clearLogs() {}
            override fun initializeClashMode(modes: StringIterator?, current: String?) {}
            override fun setDefaultLogLevel(level: Int) {}
            override fun updateClashMode(mode: String?) {}
            override fun writeConnectionEvents(events: ConnectionEvents?) {}
            override fun writeGroups(groups: OutboundGroupIterator?) {}
            override fun writeLogs(logs: LogIterator?) {}
        }, options)
        return try {
            client.connect()
            client.urlTest(groupTag)
            val deadline = startedAt + waitMillis
            while (System.currentTimeMillis() < deadline && !answered.containsAll(expected)) Thread.sleep(250)
            answered.filter { it in expected }.toSet()
        } catch (_: Exception) {
            null
        } finally {
            runCatching { client.disconnect() }
        }
    }
}
