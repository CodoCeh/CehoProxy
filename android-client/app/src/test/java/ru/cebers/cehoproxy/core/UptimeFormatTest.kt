package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class UptimeFormatTest {
    @Test fun formatsHoursMinutesSeconds() {
        assertEquals("00:00:00", UptimeFormat.label(0L))
        assertEquals("00:00:09", UptimeFormat.label(9_999L))
        assertEquals("01:02:03", UptimeFormat.label(3_723_000L))
        assertEquals("00:00:00", UptimeFormat.label(-5L))
    }
}
