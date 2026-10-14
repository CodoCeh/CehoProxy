package ru.cebers.cehoproxy.core

object ExpiryReminderPolicy {
    private const val DAY_SECONDS = 24L * 60 * 60

    fun reminderTimes(expiryEpochSeconds: Long, nowEpochSeconds: Long): List<Long> =
        (3 downTo 1).map { expiryEpochSeconds - it * DAY_SECONDS }.filter { it > nowEpochSeconds }

    fun notificationId(expiryEpochSeconds: Long, reminderEpochSeconds: Long): Int =
        ("$expiryEpochSeconds:$reminderEpochSeconds".hashCode() and 0x7fffffff).coerceAtLeast(1)
}
