package ru.cebers.cehoproxy.core

internal object SubscriptionRefreshPolicy {
    const val HOUR_MILLIS = 60 * 60 * 1000L
    const val DEFAULT_INTERVAL_HOURS = 4
    /** 0 turns background refresh off. */
    val INTERVAL_OPTIONS_HOURS = listOf(0, 1, 4, 12, 24)
    private const val CONNECT_FALLBACK_HOURS = 24

    /** A remote subscription is refreshed when its nodes are older than the chosen interval. */
    fun isStale(url: String?, fetchedAtMillis: Long, nowMillis: Long, intervalHours: Int): Boolean =
        !url.isNullOrBlank() && nowMillis - fetchedAtMillis > maxAgeMillis(intervalHours)

    private const val EARLY_RUN_TOLERANCE_MILLIS = 10 * 60 * 1000L

    /** The interval the provider asked for in the subscription response, if any. */
    fun providerHours(profile: ClientProfile, url: String): Int? =
        profile.nodes.firstOrNull { SubscriptionMembership.belongs(it, url, profile.subscriptionUrls) }?.subscriptionMetadata?.updateIntervalHours

    /** Turning refresh off in the settings wins; otherwise the provider's interval beats the app default. */
    fun intervalHoursFor(profile: ClientProfile, url: String): Int =
        if (profile.subscriptionRefreshHours == 0) 0 else providerHours(profile, url) ?: profile.subscriptionRefreshHours

    fun fetchedAt(profile: ClientProfile, url: String): Long = profile.subscriptionFetchedAtByUrl[url] ?: profile.subscriptionFetchedAt

    /** Subscriptions whose own interval has passed; the background job runs at the shortest interval and updates only these. */
    fun dueUrls(profile: ClientProfile, nowMillis: Long): List<String> = profile.subscriptionUrls.filter { url ->
        val hours = intervalHoursFor(profile, url)
        hours > 0 && nowMillis - fetchedAt(profile, url) >= hours * HOUR_MILLIS - EARLY_RUN_TOLERANCE_MILLIS
    }

    /** Subscriptions to refresh before connecting. */
    fun staleUrls(profile: ClientProfile, nowMillis: Long): List<String> = profile.subscriptionUrls.filter { url ->
        isStale(url, fetchedAt(profile, url), nowMillis, intervalHoursFor(profile, url))
    }

    fun backgroundHours(profile: ClientProfile): Int =
        profile.subscriptionUrls.map { intervalHoursFor(profile, it) }.filter { it > 0 }.minOrNull() ?: 0

    fun maxAgeMillis(intervalHours: Int): Long = (if (intervalHours > 0) intervalHours else CONNECT_FALLBACK_HOURS) * HOUR_MILLIS

    fun applyFetchedSources(profile: ClientProfile, fetched: Map<String, List<ProxyNode>>, nowMillis: Long): ClientProfile {
        val urls = profile.subscriptionUrls.filter { it in fetched }
        if (urls.isEmpty()) return profile
        // Each subscription keeps its own copy of a node, even when another subscription delivers the same config.
        val fresh = urls.flatMap { url -> fetched.getValue(url).distinctBy { it.raw } }
        val freshRaws = fresh.map { it.raw }.toSet()
        val kept = profile.nodes.filterNot { node ->
            urls.any { SubscriptionMembership.belongs(node, it, profile.subscriptionUrls) } ||
                (node.subscriptionMetadata.sourceUrl == null && node.raw in freshRaws)
        }
        val nodes = kept + fresh
        return profile.copy(nodes = nodes, selected = reselect(profile.selectedNode, nodes), subscriptionFetchedAt = nowMillis,
            subscriptionFetchedAtByUrl = (profile.subscriptionFetchedAtByUrl + urls.associateWith { nowMillis }).filterKeys { it in profile.subscriptionUrls })
    }

    fun fetchedLabel(atMillis: Long, nowMillis: Long, zone: java.time.ZoneId = java.time.ZoneId.systemDefault()): String {
        val at = java.time.Instant.ofEpochMilli(atMillis).atZone(zone)
        val now = java.time.Instant.ofEpochMilli(nowMillis).atZone(zone)
        val pattern = if (at.toLocalDate() == now.toLocalDate()) "HH:mm" else "dd.MM HH:mm"
        return "Обновлено " + at.format(java.time.format.DateTimeFormatter.ofPattern(pattern))
    }

    fun reselect(previous: ProxyNode?, nodes: List<ProxyNode>): Int {
        if (previous == null) return 0
        val byRaw = nodes.indexOfFirst { it.raw == previous.raw }
        if (byRaw >= 0) return byRaw
        val byName = nodes.indexOfFirst { it.name == previous.name && it.subscriptionMetadata.sourceHost == previous.subscriptionMetadata.sourceHost }
        if (byName >= 0) return byName
        return nodes.indexOfFirst { it.server == previous.server && it.port == previous.port }.takeIf { it >= 0 } ?: 0
    }
}
