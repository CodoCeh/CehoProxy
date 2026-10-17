package ru.cebers.cehoproxy.core

internal object FailoverHealthPolicy {
    const val FAILED_ROUNDS_BEFORE_ERROR = 2
    const val STARTUP_GRACE_MILLIS = 60_000L
    const val RETRY_DELAY_MILLIS = 3_000L

    fun nextFailedRounds(previous: Int, healthy: Boolean, sinceStartMillis: Long): Int = when {
        healthy -> 0
        sinceStartMillis < STARTUP_GRACE_MILLIS -> previous
        else -> previous + 1
    }

    fun showsError(failedRounds: Int): Boolean = failedRounds >= FAILED_ROUNDS_BEFORE_ERROR
}
