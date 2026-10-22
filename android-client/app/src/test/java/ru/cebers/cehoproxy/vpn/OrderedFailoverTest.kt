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

    @Test fun recentlyFailedNodeIsTriedAfterTheOthers() {
        val memory = NodeHealthMemory()
        memory.markBad("one")
        val probes = mutableListOf<String>()
        val selected = OrderedFailover.firstWorking(listOf("one", "two", "three"), { true }, memory, 3_000) { probes.add(it); 100L }
        assertEquals("two", selected)
        assertEquals(listOf("two"), probes)
    }

    @Test fun failedProbeMarksNodeBadAndAnswerClearsIt() {
        val memory = NodeHealthMemory()
        OrderedFailover.firstWorking(listOf("one", "two"), { true }, memory, 3_000) { if (it == "one") null else 100L }
        assertTrue(memory.isBad("one"))
        assertFalse(memory.isBad("two"))
        OrderedFailover.firstWorking(listOf("one"), { true }, memory, 3_000) { 100L }
        assertFalse(memory.isBad("one"))
    }

    @Test fun badMarkExpires() {
        var now = 0L
        val memory = NodeHealthMemory { now }
        memory.markBad("one")
        now = NodeHealthMemory.BAD_MILLIS - 1
        assertTrue(memory.isBad("one"))
        now = NodeHealthMemory.BAD_MILLIS + 1
        assertFalse(memory.isBad("one"))
    }

    @Test fun slowNodeIsUsedOnlyWhenNoQuickerNodeAnswers() {
        val memory = NodeHealthMemory()
        val answers = mapOf("slow" to 3_900L, "quick" to 400L)
        assertEquals("quick", OrderedFailover.firstWorking(listOf("slow", "quick"), { true }, memory, 3_000) { answers[it] })
        assertEquals("slow", OrderedFailover.firstWorking(listOf("slow", "dead"), { true }, memory, 3_000) { answers[it] })
    }

    @Test fun allNodesBadStillGetsAnotherTryInOriginalOrder() {
        val memory = NodeHealthMemory()
        memory.markBad("one"); memory.markBad("two")
        assertEquals("one", OrderedFailover.firstWorking(listOf("one", "two"), { true }, memory, 3_000) { 100L })
    }

    @Test fun noWorkingNodeReturnsNullWithoutDirectFallback() {
        assertNull(OrderedFailover.firstWorking(listOf("one", "two"), { true }, NodeHealthMemory(), 3_000) { null })
    }

    @Test fun afterScoutOnlyAnsweringNodesAreProbed() {
        assertEquals(listOf("b", "d"), OrderedFailover.afterScout(listOf("a", "b", "c", "d"), setOf("d", "b")))
    }

    @Test fun afterScoutNoAnswerLeavesOneSanityProbe() {
        assertEquals(listOf("a"), OrderedFailover.afterScout(listOf("a", "b", "c"), emptySet()))
    }

    @Test fun afterScoutWithoutResultKeepsTheWholeList() {
        assertEquals(listOf("a", "b"), OrderedFailover.afterScout(listOf("a", "b"), null))
    }
}
