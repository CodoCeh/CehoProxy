package ru.cebers.cehoproxy.core

/** Decides when a tunnel that stopped answering is restarted without the user. */
internal object StuckTunnelPolicy {
    const val FAILED_CHECKS_BEFORE_RESTART = 3
    const val MIN_RESTART_INTERVAL_MILLIS = 120_000L

    fun shouldRestart(failedChecks: Int, nowMillis: Long, lastRestartMillis: Long): Boolean =
        failedChecks >= FAILED_CHECKS_BEFORE_RESTART && nowMillis - lastRestartMillis >= MIN_RESTART_INTERVAL_MILLIS
}
