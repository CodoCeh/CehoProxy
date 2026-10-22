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

    @Test fun spokenFormGrowsFromMinutesToDays() {
        assertEquals("меньше минуты", UptimeFormat.words(59_000L))
        assertEquals("1 минута", UptimeFormat.words(60_000L))
        assertEquals("24 минуты", UptimeFormat.words(24 * 60_000L))
        assertEquals("11 минут", UptimeFormat.words(11 * 60_000L))
        assertEquals("1 ч 05 мин", UptimeFormat.words(65 * 60_000L))
        assertEquals("2 дня 3 ч", UptimeFormat.words((2 * 24 * 60 + 3 * 60) * 60_000L))
        assertEquals("меньше минуты", UptimeFormat.words(-1L))
    }
}
