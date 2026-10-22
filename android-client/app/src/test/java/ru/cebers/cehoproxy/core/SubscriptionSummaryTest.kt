package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import ru.cebers.cehoproxy.core.SubscriptionSummary.TermKind
import ru.cebers.cehoproxy.core.SubscriptionSummary.TrafficKind

class SubscriptionSummaryTest {
    private val now = 1_800_000_000L

    @Test fun unknownTermIsNotShownAsZeroDays() {
        assertEquals(TermKind.UNKNOWN, SubscriptionSummary.term(null, now).kind)
        assertEquals(TermKind.UNKNOWN, SubscriptionSummary.term(0, now).kind)
        assertNull(SubscriptionSummary.term(null, now).days)
    }

    @Test fun wholeDaysAreCountedFromRealTime() {
        val term = SubscriptionSummary.term(now + 18 * 86_400L + 100, now)
        assertEquals(TermKind.ACTIVE, term.kind)
        assertEquals("18 дней", term.label)
        assertEquals(now + 18 * 86_400L + 100, term.expiresAt)
    }

    @Test fun lessThanOneDayAndExpiredAreSeparateStates() {
        assertEquals(TermKind.LAST_DAY, SubscriptionSummary.term(now + 3_600, now).kind)
        assertEquals(TermKind.EXPIRED, SubscriptionSummary.term(now, now).kind)
        assertEquals(TermKind.EXPIRED, SubscriptionSummary.term(now - 5, now).kind)
    }

    @Test fun russianPluralForms() {
        assertEquals("1 день", SubscriptionSummary.term(now + 86_400 + 5, now).label)
        assertEquals("2 дня", SubscriptionSummary.term(now + 2 * 86_400 + 5, now).label)
        assertEquals("11 дней", SubscriptionSummary.term(now + 11 * 86_400 + 5, now).label)
        assertEquals("21 день", SubscriptionSummary.term(now + 21 * 86_400 + 5, now).label)
        assertEquals("112 дней", SubscriptionSummary.term(now + 112 * 86_400 + 5, now).label)
    }

    @Test fun limitedTrafficHasHonestShareAndRemainder() {
        val traffic = SubscriptionSummary.traffic(25, 100)
        assertEquals(TrafficKind.LIMITED, traffic.kind)
        assertEquals(.25f, traffic.fraction)
        assertEquals(75L, traffic.remaining)
    }

    @Test fun unknownLimitIsNotUnlimited() {
        val traffic = SubscriptionSummary.traffic(500, null)
        assertEquals(TrafficKind.UNKNOWN_LIMIT, traffic.kind)
        assertNull(traffic.fraction)
        assertNull(traffic.remaining)
    }

    @Test fun zeroLimitFromProviderMeansUnlimitedWithoutDivision() {
        val traffic = SubscriptionSummary.traffic(500, 0)
        assertEquals(TrafficKind.UNLIMITED, traffic.kind)
        assertNull(traffic.fraction)
    }

    @Test fun usageAboveLimitIsCappedAndNeverNegative() {
        val traffic = SubscriptionSummary.traffic(150, 100)
        assertEquals(TrafficKind.OVER_LIMIT, traffic.kind)
        assertEquals(1f, traffic.fraction)
        assertEquals(0L, traffic.remaining)
    }

    @Test fun limitWithoutUsageAndNoDataAtAll() {
        assertEquals(TrafficKind.LIMIT_ONLY, SubscriptionSummary.traffic(null, 100).kind)
        assertEquals(TrafficKind.NONE, SubscriptionSummary.traffic(null, null).kind)
    }

    @Test fun byteFormatting() {
        java.util.Locale.setDefault(java.util.Locale.US)
        assertEquals("512 Б", SubscriptionSummary.bytes(512))
        assertEquals("1.5 КиБ", SubscriptionSummary.bytes(1536))
        assertEquals("28.0 ГиБ", SubscriptionSummary.bytes(28L * 1024 * 1024 * 1024))
    }
}
