package ru.cebers.cehoproxy.vpn

internal enum class NetworkMonitorStrategy {
    BEST_MATCHING,
    REQUEST,
    DEFAULT_WITH_HANDLER,
    DEFAULT;

    companion object {
        fun forApi(apiLevel: Int): NetworkMonitorStrategy = when {
            apiLevel >= 31 -> BEST_MATCHING
            apiLevel >= 28 -> REQUEST
            apiLevel >= 26 -> DEFAULT_WITH_HANDLER
            else -> DEFAULT
        }
    }
}
