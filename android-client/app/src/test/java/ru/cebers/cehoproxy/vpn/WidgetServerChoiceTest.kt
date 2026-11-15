package ru.cebers.cehoproxy.vpn

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class WidgetServerChoiceTest {
    @Test fun movesToTheNextAllowedServerAndWraps() {
        assertEquals(3, WidgetServerChoice.next(listOf(0, 3, 5), 1))
        assertEquals(0, WidgetServerChoice.next(listOf(0, 3, 5), 5))
        assertEquals(0, WidgetServerChoice.next(listOf(0), 2))
        assertNull(WidgetServerChoice.next(listOf(4), 4))
        assertNull(WidgetServerChoice.next(emptyList(), 0))
    }
}
