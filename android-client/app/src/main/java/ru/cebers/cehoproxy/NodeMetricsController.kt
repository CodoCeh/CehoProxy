package ru.cebers.cehoproxy

import android.os.Handler
import android.os.Looper
import android.util.Log
import io.nekohasekai.libbox.*
import kotlinx.coroutines.*
import ru.cebers.cehoproxy.core.*
import java.util.concurrent.atomic.AtomicLong

private const val COUNTRY_TIMEOUT_MS = 15_000L
private const val LATENCY_ATTEMPTS = 2
private const val ENGINE_WAIT_MS = 10_000L
private const val LATENCY_ATTEMPT_WAIT_MS = 6_000L

/** Uses the running engine's named outbound; never selects it or changes VPN app scope. */
internal class NodeMetricsController(context: android.content.Context, private val publish: (VpnMeasurements) -> Unit) {
    private val appContext = context.applicationContext
    private val countryCache = NodeCountryCache(context)
    private val main = Handler(Looper.getMainLooper())
    private val generation = AtomicLong()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var client: CommandClient? = null
    private var session: NetworkQualityTestSession? = null
    private var watchdog: Job? = null
    private var reconnectJob: Job? = null
    private val reconnect = MetricsReconnectPolicy()
    private var tags: Map<String, String> = emptyMap()
    private var nodeRaws: Map<String, String> = emptyMap()
    private var nodesById: Map<String, ProxyNode> = emptyMap()
    private var fingerprint: List<String>? = null
    private var measurement = VpnMeasurements()
    private var previousTotals: Triple<Long, Long, Long>? = null
    private var probeId = 0L
    private var latencyJob: Job? = null
    private var recheckJob: Job? = null
    private var waitingForEngine: List<String>? = null
    private var latencyStartedAt = 0L
    private var selectedRouteId: String? = null
    private val countryInFlight = mutableSetOf<String>()
    private var flaglessRouteIds: Set<String> = emptySet()
    private val flagQueue = ArrayDeque<String>()

    fun restoreCountries(profile: ClientProfile) {
        val now = System.currentTimeMillis()
        val nodes = profile.nodes.associate { node ->
            val previous = measurement.nodes[node.routeId] ?: NodeMeasurement()
            val country = previous.verifiedCountry?.takeIf { NodeCountryCachePolicy.isFresh(it, now) } ?: countryCache.read(node.routeId, now)
            node.routeId to previous.copy(verifiedCountry = country)
        }
        measurement = measurement.copy(nodes = nodes)
        emit()
    }

    fun start(profile: ClientProfile) {
        if (!profile.importedConfig.isNullOrBlank()) { stop(); return }
        flaglessRouteIds = profile.nodes.filter { NodeCountry.resolve(it.name) == null }.map { it.routeId }.toSet()
        val nextTags = NodeMetricsPolicy.tags(profile)
        val nextFingerprint = nextTags.map { (id, tag) -> "$id=$tag" }
        if (reconnect.active && fingerprint == nextFingerprint) {
            selectedRouteId = profile.selectedNode?.routeId
            return
        }
        stop()
        selectedRouteId = profile.selectedNode?.routeId
        tags = nextTags
        nodeRaws = profile.nodes.associate { it.routeId to it.raw }
        nodesById = profile.nodes.associateBy { it.routeId }
        measurement = measurement.copy(nodes = measurement.nodes.filterKeys { it in nextTags })
        fingerprint = nextFingerprint
        reconnect.start()
        connectClient()
    }

    private fun connectClient() {
        if (!reconnect.active) return
        val token = generation.get()
        val options = CommandClientOptions().apply {
            statusInterval = 1_000_000_000L
            addCommand(Libbox.CommandStatus)
            addCommand(Libbox.CommandOutbounds)
        }
        val native = CommandClient(object : CommandClientHandler {
            override fun connected() = post(token) {
                measurement = measurement.copy(connected = true); emit(); queueFlags()
                waitingForEngine?.let { ids -> waitingForEngine = null; checkLatency(ids) }
            }
            override fun disconnected(message: String?) = post(token) { connectionLost() }
            override fun writeStatus(status: StatusMessage) {
                val down = status.downlinkTotal
                val up = status.uplinkTotal
                val now = System.nanoTime()
                val available = status.trafficAvailable
                post(token) {
                    reconnect.receivedData()
                    val previous = previousTotals
                    previousTotals = Triple(down, up, now)
                    measurement = measurement.copy(
                        downloadBitsPerSecond = if (available && previous != null) NodeMetricsPolicy.rate(previous.first, down, now - previous.third) else null,
                        uploadBitsPerSecond = if (available && previous != null) NodeMetricsPolicy.rate(previous.second, up, now - previous.third) else null,
                    )
                    emit()
                }
            }
            override fun writeOutbounds(items: OutboundGroupItemIterator) {
                val results = mutableMapOf<String, Pair<Int, Long>>()
                while (items.hasNext()) {
                    val item = items.next()
                    if (item.urlTestTime > 0) results[item.tag] = item.urlTestDelay to item.urlTestTime * 1000
                }
                post(token) {
                    reconnect.receivedData()
                    val updates = tags.mapNotNull { (id, tag) ->
                        val (delay, at) = results[tag] ?: return@mapNotNull null
                        val current = measurement.nodes[id] ?: NodeMeasurement()
                        // The engine stores whole seconds, so a fast answer in the second the test began must still count.
                        if (current.testing && at + 1000 <= latencyStartedAt) return@mapNotNull null
                        NodeMetricsPolicy.applyUrlTest(current, delay, at)?.let { id to it }
                    }.toMap()
                    measurement = measurement.copy(nodes = measurement.nodes + updates)
                    emit()
                }
            }
            override fun clearLogs() {}
            override fun initializeClashMode(modes: StringIterator?, current: String?) {}
            override fun setDefaultLogLevel(level: Int) {}
            override fun updateClashMode(mode: String?) {}
            override fun writeConnectionEvents(events: ConnectionEvents?) {}
            override fun writeGroups(groups: OutboundGroupIterator?) {}
            override fun writeLogs(logs: LogIterator?) {}
        }, options)
        client = native
        scope.launch { runCatching { if (generation.get() == token) native.connect(); if (generation.get() != token) native.disconnect() }.onFailure { post(token) { connectionLost() } } }
    }

    private fun connectionLost() {
        val retryDelay = reconnect.disconnected() ?: return
        val token = generation.incrementAndGet()
        countryInFlight.clear()
        cancelProbe()
        val previous = client
        client = null
        previousTotals = null
        measurement = measurement.copy(connected = false, downloadBitsPerSecond = null, uploadBitsPerSecond = null)
        emit()
        if (previous != null) scope.launch { runCatching { previous.disconnect() } }
        reconnectJob?.cancel()
        reconnectJob = scope.launch {
            delay(retryDelay)
            post(token) { if (reconnect.retryReady()) connectClient() }
        }
    }

    private fun requestCountry(routeId: String) {
        val raw = nodeRaws[routeId] ?: return
        if (!countryInFlight.add(routeId)) return
        val token = generation.get()
        main.postDelayed({ if (generation.get() == token && countryInFlight.remove(routeId)) drainFlags() }, COUNTRY_TIMEOUT_MS)
        val queued = ru.cebers.cehoproxy.vpn.CehoVpnService.requestNodeExitCountry(raw) { countryCode, exitIp ->
            val country = if (countryCode != null && exitIp != null) NodeExitCountryProbe.parse(org.json.JSONObject().put("country", countryCode).put("ip", exitIp).toString())?.copy(checkedAt = System.currentTimeMillis()) else null
            post(token) {
                countryInFlight.remove(routeId)
                if (country != null) {
                    if (nodeRaws[routeId] == raw) countryCache.save(routeId, country)
                    measurement = measurement.copy(nodes = NodeMetricsPolicy.applyVerifiedCountry(measurement.nodes, nodeRaws, routeId, raw, country))
                    emit()
                }
                drainFlags()
            }
        }
        if (!queued) countryInFlight.remove(routeId)
    }

    /** Parallel URL test of the whole group, repeated once for nodes that did not answer; a node still silent after that is marked failed. */
    fun checkLatency(routeIds: List<String>) {
        val ids = routeIds.filter { it in tags }
        if (ids.isEmpty()) return
        val native = client
        if (native == null || !measurement.connected) {
            // The VPN is up but this app has not attached to the engine yet: run the test as soon as it does instead of reporting silence.
            waitingForEngine = ids
            measurement = measurement.copy(nodes = measurement.nodes + ids.associateWith { NodeMetricsPolicy.beginCheck(measurement.nodes[it]) })
            emit()
            val token = generation.get()
            scope.launch {
                delay(ENGINE_WAIT_MS)
                post(token) {
                    if (waitingForEngine == ids) {
                        waitingForEngine = null
                        measurement = measurement.copy(nodes = measurement.nodes.mapValues { (id, item) -> if (id in ids && item.testing) NodeMeasurement(failed = true, failureReason = "Движок измерения недоступен", measuredAt = System.currentTimeMillis(), verifiedCountry = item.verifiedCountry) else item })
                        emit()
                    }
                }
            }
            return
        }
        stopProbe()
        latencyJob?.cancel()
        recheckJob?.cancel()
        val token = generation.get()
        latencyStartedAt = System.currentTimeMillis()
        measurement = measurement.copy(nodes = measurement.nodes + ids.associateWith { NodeMetricsPolicy.beginCheck(measurement.nodes[it]) })
        emit()
        latencyJob = scope.launch {
            repeat(LATENCY_ATTEMPTS) { attempt ->
                if (attempt > 0 && ids.none { measurement.nodes[it]?.testing == true }) return@repeat
                runCatching { native.urlTest(OrderedFailoverConfig.LATENCY_TAG) }
                delay(LATENCY_ATTEMPT_WAIT_MS)
            }
            post(token) {
                val silent = ids.filter { measurement.nodes[it]?.testing == true }
                measurement = measurement.copy(nodes = measurement.nodes.mapValues { (id, item) ->
                    if (id in silent) NodeMeasurement(failed = true, failureReason = "Нет ответа", measuredAt = System.currentTimeMillis(), verifiedCountry = item.verifiedCountry) else item
                })
                emit()
                recheckOneByOne(silent, token)
            }
        }
    }

    /**
     * Many simultaneous handshakes to one provider can be throttled, so every node that stayed silent in the burst gets its own
     * request through a dedicated path before it is called dead.
     */
    private fun recheckOneByOne(ids: List<String>, token: Long) {
        val port = appContext.getSharedPreferences("vpn_status", android.content.Context.MODE_PRIVATE).getInt("check_port", 0)
        val native = client
        if (ids.isEmpty() || port <= 0 || native == null) { explainSilence(ids, token); return }
        post(token) { measurement = measurement.copy(nodes = measurement.nodes + ids.associateWith { NodeMetricsPolicy.beginCheck(measurement.nodes[it]) }); emit() }
        recheckJob?.cancel()
        recheckJob = scope.launch {
            val stillSilent = mutableListOf<String>()
            for (id in ids) {
                if (generation.get() != token) return@launch
                val tag = tags[id] ?: continue
                val answeredIn = runCatching { native.selectOutbound(OrderedFailoverConfig.CHECK_TAG, tag) }.map { httpProbe(port) }.getOrNull()
                post(token) {
                    val current = measurement.nodes[id] ?: NodeMeasurement()
                    measurement = measurement.copy(nodes = measurement.nodes + (id to
                        if (answeredIn != null) current.copy(latencyMs = answeredIn, failed = false, failureReason = null, testing = false, measuredAt = System.currentTimeMillis())
                        else NodeMeasurement(failed = true, failureReason = "Нет ответа", measuredAt = System.currentTimeMillis(), verifiedCountry = current.verifiedCountry)))
                    emit()
                }
                if (answeredIn == null) stillSilent += id
            }
            post(token) { explainSilence(stillSilent, token) }
        }
    }

    private fun httpProbe(port: Int): Int? {
        val started = System.nanoTime()
        val connection = java.net.URL(OrderedFailoverConfig.LATENCY_URL).openConnection(java.net.Proxy(java.net.Proxy.Type.HTTP, java.net.InetSocketAddress("127.0.0.1", port))) as java.net.HttpURLConnection
        connection.connectTimeout = 4000
        connection.readTimeout = 4000
        connection.instanceFollowRedirects = false
        connection.useCaches = false
        return try {
            if (connection.responseCode in 200..399) ((System.nanoTime() - started) / 1_000_000).toInt().coerceAtLeast(1) else null
        } catch (_: java.io.IOException) { null } finally { connection.disconnect() }
    }

    /** Tells apart a server that is down from one whose port is open but passes no traffic. */
    private fun explainSilence(ids: List<String>, token: Long) {
        ids.forEach { id ->
            val node = nodesById[id] ?: return@forEach
            if (NodeReachability.isUdp(node)) return@forEach
            scope.launch {
                val tcp = NodeReachability.check(node, System.currentTimeMillis())
                val reason = if (!tcp.failed) "Порт открыт, но трафик не проходит" else tcp.failureReason
                post(token) {
                    val current = measurement.nodes[id] ?: return@post
                    if (current.failed && current.failureReason == "Нет ответа") {
                        measurement = measurement.copy(nodes = measurement.nodes + (id to current.copy(failureReason = reason)))
                        emit()
                    }
                }
            }
        }
    }

    fun check(routeId: String) {
        startCheck(routeId)
    }

    private fun startCheck(routeId: String): Boolean {
        val native = client ?: return false
        val tag = tags[routeId] ?: return false
        if (!measurement.connected) return false
        latencyJob?.cancel()
        stopProbe()
        val token = generation.get()
        val probe = ++probeId
        val startedAt = System.nanoTime()
        requestCountry(routeId)
        val phase = java.util.concurrent.atomic.AtomicInteger(-1)
        Log.i("CehoNodeMetrics", "probe=$probe started runtime_seconds=20 watchdog_seconds=35")
        measurement = measurement.copy(nodes = measurement.nodes + (routeId to NodeMetricsPolicy.beginCheck(measurement.nodes[routeId])))
        emit()
        // Covers configuration download as well as the native 20-second measurement window.
        watchdog = scope.launch { delay(35_000); post(token) { if (probeId == probe) { stopProbe(); failMeasurement(routeId, "watchdog: deadline exceeded", probe, startedAt) } } }
        scope.launch {
            runCatching {
                native.startNetworkQualityTest(Libbox.NetworkQualityDefaultConfigURL, tag, false, 20, false, object : NetworkQualityTestHandler {
                    override fun onProgress(progress: NetworkQualityProgress?) {
                        progress?.let { if (phase.getAndSet(it.phase) != it.phase) Log.i("CehoNodeMetrics", "probe=$probe phase=${it.phase} elapsed_ms=${it.elapsedMs}") }
                    }
                    override fun onError(message: String?) = post(token) { if (probeId == probe) { watchdog?.cancel(); failMeasurement(routeId, message, probe, startedAt) } }
                    override fun onResult(result: NetworkQualityResult) {
                        val value = NodeMeasurement(result.idleLatencyMs.takeIf { it > 0 }, result.downloadCapacity.takeIf { it > 0 }, result.uploadCapacity.takeIf { it > 0 }, measuredAt = System.currentTimeMillis())
                        post(token) { if (probeId == probe) { watchdog?.cancel(); Log.i("CehoNodeMetrics", "probe=$probe completed elapsed_ms=${(System.nanoTime() - startedAt) / 1_000_000}"); finish(routeId, value) } }
                    }
                })
            }.onSuccess { created -> main.post { if (generation.get() == token && probeId == probe) session = created else scope.launch { runCatching { created.close() } } } }
                .onFailure { error -> post(token) { if (probeId == probe) { watchdog?.cancel(); failMeasurement(routeId, error.message, probe, startedAt) } } }
        }
        return true
    }

    private fun failMeasurement(id: String, message: String?, probe: Long, startedAt: Long) {
        val failure = NodeMeasurementFailure.from(message)
        Log.w("CehoNodeMetrics", "probe=$probe ${failure.safeLog} elapsed_ms=${(System.nanoTime() - startedAt) / 1_000_000}")
        finish(id, NodeMeasurement(failed = true, failureReason = failure.description))
    }

    private fun queueFlags() {
        flagQueue.clear()
        flagQueue.addAll(flaglessRouteIds)
        drainFlags()
    }

    private fun drainFlags() {
        if (!measurement.connected || countryInFlight.isNotEmpty() || measurement.nodes.values.any { it.testing }) return
        val now = System.currentTimeMillis()
        while (flagQueue.isNotEmpty()) {
            val id = flagQueue.removeFirst()
            if (measurement.nodes[id]?.verifiedCountry?.let { NodeCountryCachePolicy.isFresh(it, now) } == true) continue
            requestCountry(id)
            return
        }
    }

    fun cancelProbe() {
        waitingForEngine = null
        latencyJob?.cancel()
        recheckJob?.cancel()
        stopProbe()
    }

    private fun stopProbe() {
        probeId++
        flagQueue.clear()
        watchdog?.cancel()
        val previous = session
        session = null
        if (previous != null) scope.launch { runCatching { previous.close() } }
        measurement = measurement.copy(nodes = measurement.nodes.mapValues { (_, item) -> if (item.testing) item.copy(testing = false) else item })
        emit()
    }

    fun stop() {
        reconnect.stop()
        reconnectJob?.cancel()
        reconnectJob = null
        generation.incrementAndGet()
        countryInFlight.clear()
        cancelProbe()
        val previous = client
        client = null
        fingerprint = null
        previousTotals = null
        measurement = measurement.copy(connected = false, downloadBitsPerSecond = null, uploadBitsPerSecond = null)
        emit()
        if (previous != null) scope.launch { runCatching { previous.disconnect() } }
    }

    fun destroy() { stop() }
    private fun finish(id: String, value: NodeMeasurement) {
        measurement = measurement.copy(nodes = measurement.nodes + (id to value.copy(verifiedCountry = value.verifiedCountry ?: measurement.nodes[id]?.verifiedCountry)))
        emit()
    }
    private fun emit() = publish(measurement)
    private fun post(token: Long, action: () -> Unit) { main.post { if (generation.get() == token) action() } }
}
