package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class ExpiryReminderPolicyTest {
    @Test fun schedulesOnlyFutureDailyRemindersForThreeDaysBeforeExpiry() {
        val expiry = 2_000_000_000L
        assertEquals(listOf(expiry - 3 * 86400, expiry - 2 * 86400, expiry - 86400), ExpiryReminderPolicy.reminderTimes(expiry, expiry - 4 * 86400))
        assertEquals(listOf(expiry - 86400), ExpiryReminderPolicy.reminderTimes(expiry, expiry - 2 * 86400))
        assertEquals(emptyList<Long>(), ExpiryReminderPolicy.reminderTimes(expiry, expiry))
    }
}
