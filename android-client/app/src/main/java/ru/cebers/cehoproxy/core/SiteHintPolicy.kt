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

    /** An application can be sent around the tunnel only in the mode where the list holds the excluded applications. */
    fun appApplies(excludeSelectedApps: Boolean, listed: List<String>, packageName: String): Boolean =
        excludeSelectedApps && packageName !in listed

    fun due(snoozedUntil: Long, lastOfferMillis: Long, nowMillis: Long): Boolean =
        nowMillis >= snoozedUntil && nowMillis - lastOfferMillis >= MIN_GAP_MILLIS

    /** Connection that went directly and closed without a single byte coming back from the site. */
    fun failedDirect(outboundType: String, downloaded: Long, uploaded: Long): Boolean =
        outboundType == "direct" && downloaded == 0L && uploaded > 0L

    /** The offer makes sense when the site goes around the tunnel now and the tunnel has a server to carry it. */
    fun tunnelApplies(onlySites: Boolean, listed: List<String>, routed: Set<String>, hasNodes: Boolean, site: String): Boolean {
        if (!hasNodes) return false
        val known = listed.map(RoutingConfig::normalizeDomain)
        val covered = known.any { site == it || site.endsWith(".$it") }
        val assigned = routed.map(RoutingConfig::normalizeDomain).any { site == it || site.endsWith(".$it") }
        if (assigned) return false
        return if (onlySites) !covered else true
    }

    /** Connection that closed without a single byte coming back from the site. */
    fun failed(outboundType: String, downloaded: Long, uploaded: Long): Boolean =
        outboundType !in setOf("direct", "block", "dns") && downloaded == 0L && uploaded > 0L
}
