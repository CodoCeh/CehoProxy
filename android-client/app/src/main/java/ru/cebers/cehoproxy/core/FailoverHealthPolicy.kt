package ru.cebers.cehoproxy.core

internal object FailoverHealthPolicy {
    const val FAILED_ROUNDS_BEFORE_ERROR = 2
    const val STARTUP_GRACE_MILLIS = 60_000L
    const val RETRY_DELAY_MILLIS = 3_000L

    fun nextFailedRounds(previous: Int, healthy: Boolean, sinceStartMillis: Long, graceMillis: Long = STARTUP_GRACE_MILLIS): Int = when {
        healthy -> 0
        sinceStartMillis < graceMillis -> previous
        else -> previous + 1
    }

    // A first connection that fails a whole round, retry included, has nothing to recover from, so it reports at once.
    fun showsError(failedRounds: Int, firstConnection: Boolean = false): Boolean =
        failedRounds >= if (firstConnection) 1 else FAILED_ROUNDS_BEFORE_ERROR
}
