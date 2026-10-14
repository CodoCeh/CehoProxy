package ru.cebers.cehoproxy.vpn

import org.junit.Assert.*
import org.junit.Test

class OrderedFailoverTest {
    @Test fun skipsDeadFirstAndStopsAtFirstHealthyWithoutTestingFasterLaterNode() {
        val probes = mutableListOf<String>()
        val selected = OrderedFailover.firstHealthy(listOf("dead", "healthy", "faster"), { true }) {
            probes.add(it)
            it != "dead"
        }
        assertEquals("healthy", selected)
        assertEquals(listOf("dead", "healthy"), probes)
    }

    @Test fun allDeadHasNoDirectFallback() {
        assertNull(OrderedFailover.firstHealthy(listOf("one", "two"), { true }) { false })
    }

    @Test fun invalidationDuringProbeCannotSelectStaleNode() {
        val generation = StartGeneration()
        val start = generation.beginStart()
        val probes = mutableListOf<String>()
        val selected = OrderedFailover.firstHealthy(listOf("one", "two"), { generation.isCurrent(start) }) {
            probes.add(it)
            generation.invalidate()
            true
        }
        assertNull(selected)
        assertEquals(listOf("one"), probes)
    }

    @Test fun recoveryPrefersOriginalSubscriptionOrderAgain() {
        assertEquals("two", OrderedFailover.firstHealthy(listOf("one", "two"), { true }) { it == "two" })
        assertEquals("one", OrderedFailover.firstHealthy(listOf("one", "two"), { true }) { true })
    }
}
