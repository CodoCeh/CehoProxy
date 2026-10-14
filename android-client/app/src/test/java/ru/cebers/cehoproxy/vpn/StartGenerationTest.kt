package ru.cebers.cehoproxy.vpn

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class StartGenerationTest {
    @Test
    fun stopInvalidatesInFlightStart() {
        val state = StartGeneration()
        val start = state.beginStart()

        state.invalidate()

        assertFalse(state.isCurrent(start))
    }

    @Test
    fun newestStartWinsAcrossRapidStartStopStart() {
        val state = StartGeneration()
        val firstStart = state.beginStart()
        state.invalidate()
        val restarted = state.beginStart()

        assertFalse(state.isCurrent(firstStart))
        assertTrue(state.isCurrent(restarted))
    }

    @Test
    fun repeatedStartsSupersedeEarlierCompletions() {
        val state = StartGeneration()
        val first = state.beginStart()
        val second = state.beginStart()
        val third = state.beginStart()

        assertFalse(state.isCurrent(first))
        assertFalse(state.isCurrent(second))
        assertTrue(state.isCurrent(third))
    }

    @Test
    fun staleCompletionCannotPublishConnected() {
        val state = StartGeneration()
        val start = state.beginStart()
        state.invalidate()
        var connectedPublished = false

        val ran = state.runIfCurrent(start) { connectedPublished = true }

        assertFalse(ran)
        assertFalse(connectedPublished)
    }

    @Test
    fun currentCompletionCanPublishConnected() {
        val state = StartGeneration()
        val start = state.beginStart()
        var connectedPublished = false

        val ran = state.runIfCurrent(start) { connectedPublished = true }

        assertTrue(ran)
        assertTrue(connectedPublished)
    }
}
