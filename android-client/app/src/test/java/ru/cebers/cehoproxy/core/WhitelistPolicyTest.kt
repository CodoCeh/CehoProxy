package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class WhitelistPolicyTest {
    @Test fun recognisesMarkedNames() {
        assertTrue(WhitelistPolicy.isMarked("🛡 Москва"))
        assertTrue(WhitelistPolicy.isMarked("Москва БС"))
        assertTrue(WhitelistPolicy.isMarked("Москва [бс]"))
        assertTrue(WhitelistPolicy.isMarked("БС-1"))
        assertFalse(WhitelistPolicy.isMarked("Бостон"))
        assertFalse(WhitelistPolicy.isMarked("ОБС Финляндия"))
        assertFalse(WhitelistPolicy.isMarked("USA"))
    }

    @Test fun markedNodesGoFirstKeepingOrder() {
        assertEquals(listOf("b", "d", "a", "c"), WhitelistPolicy.preferMarked(listOf("a", "b", "c", "d"), setOf("d", "b")))
        assertEquals(listOf("a", "c"), WhitelistPolicy.preferMarked(listOf("a", "c"), emptySet()))
    }

    @Test fun outageTextNamesTheCause() {
        assertTrue(WhitelistPolicy.outageText(false, true).contains("нет серверов для белых списков"))
        assertTrue(WhitelistPolicy.outageText(true, false).contains("Включите автовыбор"))
        assertTrue(WhitelistPolicy.outageText(true, true).contains("сейчас не отвечают"))
    }
}
