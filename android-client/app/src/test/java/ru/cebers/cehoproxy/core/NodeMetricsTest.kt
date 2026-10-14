package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class NodeMetricsTest {
    private val a = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443#A").single()
    private val b = NodeShareParser.parse("vless://22222222-2222-2222-2222-222222222222@other.example:443#B").single()

    @Test fun metricTagsMatchActualEngineOutboundsWithDuplicateNodes() {
        val nodes = listOf(a, b, a)
        val config = JSONObject(RoutingConfig.build(nodes, 0, RoutePolicy()))
        val outbounds = config.getJSONArray("outbounds")
        val actual = (0 until outbounds.length()).map { outbounds.getJSONObject(it).getString("tag") }
        assertEquals(mapOf(a.routeId to "proxy-0", b.routeId to "proxy-1"), NodeMetricsPolicy.tags(nodes))
        NodeMetricsPolicy.tags(nodes).values.forEach { assertTrue(it in actual) }
        assertEquals("proxy", NodeMetricsPolicy.tags(listOf(a, a))[a.routeId])
    }

    @Test fun ratesUseElapsedTimeAndRejectCounterReset() {
        assertEquals(8_000_000L, NodeMetricsPolicy.rate(10, 2_000_010, 2_000_000_000))
        assertEquals(0L, NodeMetricsPolicy.rate(10, 10, 1_000_000_000))
        assertNull(NodeMetricsPolicy.rate(100, 5, 1_000_000_000))
        assertNull(NodeMetricsPolicy.rate(0, 5, 0))
    }

    @Test fun canceledRecheckRetainsLastSuccessfulNumbersAndMeasurementTime() {
        val last = NodeMeasurement(latencyMs = 438, downloadBitsPerSecond = 63_500_000, uploadBitsPerSecond = 28_900_000, measuredAt = 1_700_000_000_000)
        val checking = NodeMetricsPolicy.beginCheck(last)
        assertEquals("Проверяем…", NodeMetricsPolicy.summary(checking))
        val canceled = checking.copy(testing = false)
        assertEquals(last, canceled)
        assertEquals(NodeMetricsPolicy.summary(last), NodeMetricsPolicy.summary(canceled))
        assertFalse(NodeMetricsPolicy.beginCheck(last.copy(failed = true, failureReason = "old failure")).failed)
        assertNull(NodeMetricsPolicy.beginCheck(last.copy(failed = true, failureReason = "old failure")).failureReason)
    }

    @Test fun noMissingOrFailedMetricPretendsToBeZeroLatency() {
        assertEquals("Ещё не измерено", NodeMetricsPolicy.summary(null))
        assertEquals("Проверка не прошла", NodeMetricsPolicy.summary(NodeMeasurement(failed = true)))
        assertEquals("Проверяем…", NodeMetricsPolicy.summary(NodeMeasurement(testing = true)))
        assertEquals("35 мс · ↓ 12,0 Мбит/с", NodeMetricsPolicy.summary(NodeMeasurement(latencyMs = 35, downloadBitsPerSecond = 12_000_000)))
    }
}
