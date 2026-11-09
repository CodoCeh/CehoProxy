package ru.cebers.cehoproxy.core

/** Decides whether a failing site may be offered for the exceptions list. */
internal object SiteHintPolicy {
    const val SNOOZE_MILLIS = 7L * 24 * 60 * 60 * 1000
    const val MIN_GAP_MILLIS = 10L * 60 * 1000
    const val NEVER = Long.MAX_VALUE

    /** The offer makes sense only when the list of exceptions is what sends a site around the tunnel. */
    fun applies(onlySites: Boolean, listed: List<String>, site: String): Boolean {
        if (onlySites) return false
        return !isListed(listed, site)
    }

    /** The site, its parent domain or one of its subdomains is already in the list: nothing to offer. */
    fun isListed(listed: List<String>, site: String): Boolean =
        listed.map(RoutingConfig::normalizeDomain).any { it.isNotEmpty() && (site == it || site.endsWith(".$it") || it.endsWith(".$site")) }

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
        if (isListed(listed, site) || isListed(routed.toList(), site)) return false
        return true
    }

    /** Connection that closed without a single byte coming back from the site. */
    fun failed(outboundType: String, downloaded: Long, uploaded: Long): Boolean =
        outboundType !in setOf("direct", "block", "dns") && downloaded == 0L && uploaded > 0L
}
