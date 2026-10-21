package ru.cebers.cehoproxy.core

import java.util.Locale

internal data class NodeMeasurement(
    val latencyMs: Int? = null,
    val downloadBitsPerSecond: Long? = null,
    val uploadBitsPerSecond: Long? = null,
    val testing: Boolean = false,
    val failed: Boolean = false,
    val measuredAt: Long = 0,
    val failureReason: String? = null,
    val verifiedCountry: VerifiedNodeCountry? = null,
)

internal data class VpnMeasurements(
    val nodes: Map<String, NodeMeasurement> = emptyMap(),
    val connected: Boolean = false,
    val downloadBitsPerSecond: Long? = null,
    val uploadBitsPerSecond: Long? = null,
)

internal object NodeMetricsPolicy {
    fun applyVerifiedCountry(nodes: Map<String, NodeMeasurement>, activeRaws: Map<String, String>, routeId: String, expectedRaw: String, country: VerifiedNodeCountry?): Map<String, NodeMeasurement> {
        if (country == null || activeRaws[routeId] != expectedRaw) return nodes
        return nodes + (routeId to (nodes[routeId] ?: NodeMeasurement()).copy(verifiedCountry = country))
    }

    /** Applies a periodic URL test result unless the node already has a newer measurement. */
    fun applyUrlTest(current: NodeMeasurement, delay: Int, testedAt: Long): NodeMeasurement? = when {
        testedAt < current.measuredAt -> null
        delay > 0 -> current.copy(latencyMs = delay, measuredAt = testedAt, failed = false, failureReason = null, testing = false)
        else -> NodeMeasurement(failed = true, failureReason = "Нет ответа", measuredAt = testedAt, verifiedCountry = current.verifiedCountry)
    }

    fun beginCheck(previous: NodeMeasurement?): NodeMeasurement =
        (previous ?: NodeMeasurement()).copy(testing = true, failed = false, failureReason = null)

    /** Engine tags of the nodes that are actually running: disabled subscriptions and excluded countries are absent from the engine. */
    fun tags(profile: ClientProfile): Map<String, String> {
        val byRaw = RoutingConfig.tagsByRaw(profile.nodes, profile.excludedCountrySets, profile.disabledSubscriptions.toSet())
        return profile.nodes.filter { it.raw in byRaw }.associate { it.routeId to byRaw.getValue(it.raw) }
    }

    fun rate(previous: Long, current: Long, elapsedNanos: Long): Long? =
        if (elapsedNanos <= 0 || current < previous) null
        else ((current - previous).toDouble() * 8 * 1_000_000_000 / elapsedNanos).toLong()

    fun speed(bitsPerSecond: Long): String = String.format(Locale.forLanguageTag("ru"), "%.1f", bitsPerSecond.coerceAtLeast(0) / 1_000_000.0)

    fun summary(measurement: NodeMeasurement?): String = when {
        measurement?.testing == true -> "Проверяем…"
        measurement?.failed == true -> measurement.failureReason ?: "Проверка не прошла"
        measurement == null || measurement.latencyMs == null -> "Ещё не измерено"
        else -> buildString {
            append("${measurement.latencyMs} мс")
            measurement.downloadBitsPerSecond?.let { append(" · ↓ ${speed(it)} Мбит/с") }
            measurement.uploadBitsPerSecond?.let { append(" · ↑ ${speed(it)} Мбит/с") }
            if (measurement.measuredAt > 0) append(" · " + java.text.SimpleDateFormat("HH:mm", Locale.forLanguageTag("ru")).format(java.util.Date(measurement.measuredAt)))
        }
    }
}
