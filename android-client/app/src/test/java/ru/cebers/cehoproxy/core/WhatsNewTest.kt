package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class WhatsNewTest {
    private val fake = listOf(
        WhatsNew.Release("0.1.19", listOf(WhatsNew.Item("a", "a"))),
        WhatsNew.Release("0.1.20", listOf(WhatsNew.Item("b", "b", "Настройки"))),
        WhatsNew.Release("0.1.21", listOf(WhatsNew.Item("c", "c"))),
    )

    @Test fun onlyVersionsBetweenSeenAndInstalledNewestFirst() {
        assertEquals(listOf("b"), WhatsNew.since("0.1.19", "0.1.20", fake).map { it.title })
        assertEquals(listOf("c", "b", "a"), WhatsNew.since("0.1.18", "0.1.21", fake).map { it.title })
        assertTrue(WhatsNew.since("0.1.21", "0.1.21", fake).isEmpty())
    }

    @Test fun freshInstallSeesNothingButUpgradeWithoutMarkerSeesEverythingUpToInstalled() {
        assertTrue(WhatsNew.pending(null, "0.1.21", true, fake).isEmpty())
        assertEquals(2, WhatsNew.pending(null, "0.1.20", false, fake).size)
    }

    @Test fun numericOrderIsUsedNotTextOrder() {
        val releases = listOf(WhatsNew.Release("0.1.9", listOf(WhatsNew.Item("old", "x"))), WhatsNew.Release("0.1.10", listOf(WhatsNew.Item("new", "y"))))
        assertEquals(listOf("new"), WhatsNew.since("0.1.9", "0.1.10", releases).map { it.title })
    }
}
