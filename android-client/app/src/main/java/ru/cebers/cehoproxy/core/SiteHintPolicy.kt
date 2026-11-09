package ru.cebers.cehoproxy.core

/** Decides whether a failing site may be offered for the exceptions list. */
internal object SiteHintPolicy {
    const val SNOOZE_MILLIS = 6L * 60 * 60 * 1000
    const val MIN_GAP_MILLIS = 10L * 60 * 1000
    const val NEVER = Long.MAX_VALUE

    /** The offer makes sense only when the list of exceptions is what sends a site around the tunnel. */
    fun applies(onlySites: Boolean, listed: List<String>, site: String): Boolean {
        if (onlySites) return false
        val known = listed.map(RoutingConfig::normalizeDomain)
        return known.none { site == it || site.endsWith(".$it") }
    }

    fun due(snoozedUntil: Long, lastOfferMillis: Long, nowMillis: Long): Boolean =
        nowMillis >= snoozedUntil && nowMillis - lastOfferMillis >= MIN_GAP_MILLIS

    /** Connection that closed without a single byte coming back from the site. */
    fun failed(outboundType: String, downloaded: Long, uploaded: Long): Boolean =
        outboundType !in setOf("direct", "block", "dns") && downloaded == 0L && uploaded > 0L
}
