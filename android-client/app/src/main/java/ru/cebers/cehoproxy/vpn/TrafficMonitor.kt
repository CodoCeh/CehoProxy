package ru.cebers.cehoproxy.vpn

import android.content.Context
import io.nekohasekai.libbox.CommandClient
import io.nekohasekai.libbox.CommandClientHandler
import io.nekohasekai.libbox.CommandClientOptions
import io.nekohasekai.libbox.ConnectionEvents
import io.nekohasekai.libbox.Connections
import io.nekohasekai.libbox.LogIterator
import io.nekohasekai.libbox.OutboundGroupItemIterator
import io.nekohasekai.libbox.OutboundGroupIterator
import io.nekohasekai.libbox.StatusMessage
import io.nekohasekai.libbox.StringIterator
import ru.cebers.cehoproxy.core.ConnectionSample
import ru.cebers.cehoproxy.core.TrafficRows
import ru.cebers.cehoproxy.core.Usage
import ru.cebers.cehoproxy.core.UsageLedger
import java.time.LocalDate
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

/** Follows the engine's connection list: who is connected where and by which rule, and how much each app and subscription used. */
internal class TrafficMonitor(context: Context, private val subscriptionOfExit: (String) -> String?) : CommandClientHandler {
    private val appContext = context.applicationContext
    private val state = Connections()
    private val counted = HashMap<String, Pair<Long, Long>>()
    private val worker = Executors.newSingleThreadScheduledExecutor { task -> Thread(task, "ceho-traffic").apply { isDaemon = true } }
    @Volatile private var client: CommandClient? = null
    @Volatile private var stopped = false
    private var ledger = UsageLedger.decode(prefs(appContext).getString(LEDGER_KEY, null))
    private var dirty = false

    fun start() {
        worker.execute(::connect)
        worker.scheduleWithFixedDelay({ save() }, SAVE_SECONDS, SAVE_SECONDS, TimeUnit.SECONDS)
    }

    fun stop() {
        stopped = true
        runCatching { client?.disconnect() }
        client = null
        worker.execute { save() }
        worker.shutdown()
        latest = emptyList()
    }

    private fun connect() {
        if (stopped) return
        val options = CommandClientOptions().apply {
            addCommand(io.nekohasekai.libbox.Libbox.CommandConnections)
            statusInterval = STATUS_INTERVAL_NANOS
        }
        val next = CommandClient(this, options)
        try {
            next.connect()
            client = next
        } catch (_: Exception) {
            if (!stopped) runCatching { worker.schedule(::connect, RETRY_SECONDS, TimeUnit.SECONDS) }
        }
    }

    override fun writeConnectionEvents(events: ConnectionEvents) {
        if (stopped) return
        worker.execute { apply(events) }
    }

    private fun apply(events: ConnectionEvents) {
        state.applyEvents(events)
        val samples = ArrayList<ConnectionSample>()
        val seen = HashSet<String>()
        val iterator = state.iterator()
        val today = LocalDate.now().toString()
        while (iterator.hasNext()) {
            val connection = iterator.next()
            val packageName = connection.processInfo?.packageNames()?.toList()?.firstOrNull() ?: TrafficRows.UNKNOWN_APP
            val chain = connection.chain().toList()
            val sample = ConnectionSample(
                id = connection.id, packageName = packageName, outbound = connection.outbound, chain = chain, rule = connection.rule,
                host = connection.domain.takeIf(String::isNotBlank) ?: connection.destination,
                uplink = connection.uplinkTotal, downlink = connection.downlinkTotal, closed = connection.closedAt > 0,
            )
            samples += sample
            seen += sample.id
            val before = counted[sample.id] ?: (0L to 0L)
            val delta = Usage((sample.uplink - before.first).coerceAtLeast(0), (sample.downlink - before.second).coerceAtLeast(0))
            counted[sample.id] = sample.uplink to sample.downlink
            if (delta.total > 0 && packageName != TrafficRows.UNKNOWN_APP) {
                ledger = ledger.add(packageName, subscriptionOfExit(TrafficRows.exitTag(sample)), delta, today)
                dirty = true
            }
        }
        counted.keys.retainAll(seen)
        latest = samples
        usage = ledger.rolled(today)
    }

    private fun save() {
        if (!dirty) return
        dirty = false
        prefs(appContext).edit().putString(LEDGER_KEY, ledger.encode()).apply()
    }

    private fun StringIterator.toList(): List<String> = buildList { while (hasNext()) add(next()) }

    override fun connected() {}
    override fun disconnected(message: String?) {
        client = null
        if (!stopped) runCatching { worker.schedule(::connect, RETRY_SECONDS, TimeUnit.SECONDS) }
    }
    override fun clearLogs() {}
    override fun initializeClashMode(modeList: StringIterator?, currentMode: String?) {}
    override fun setDefaultLogLevel(level: Int) {}
    override fun updateClashMode(newMode: String?) {}
    override fun writeGroups(message: OutboundGroupIterator?) {}
    override fun writeLogs(messageList: LogIterator?) {}
    override fun writeOutbounds(message: OutboundGroupItemIterator?) {}
    override fun writeStatus(message: StatusMessage?) {}

    companion object {
        private const val PREFS = "traffic"
        private const val LEDGER_KEY = "ledger"
        private const val STATUS_INTERVAL_NANOS = 2_000_000_000L
        private const val SAVE_SECONDS = 30L
        private const val RETRY_SECONDS = 5L

        @Volatile var latest: List<ConnectionSample> = emptyList()
            private set
        @Volatile private var usage: UsageLedger? = null

        private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

        fun usage(context: Context): UsageLedger = (usage ?: UsageLedger.decode(prefs(context).getString(LEDGER_KEY, null))).rolled(LocalDate.now().toString())
    }
}
