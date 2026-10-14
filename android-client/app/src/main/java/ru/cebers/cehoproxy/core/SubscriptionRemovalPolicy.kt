package ru.cebers.cehoproxy.core

internal object SubscriptionRemovalPolicy {
    fun hostOf(url: String): String? = runCatching { java.net.URL(url).host.lowercase() }.getOrNull()

    fun urlFor(profile: ClientProfile, metadata: SubscriptionMetadata): String? {
        metadata.sourceUrl?.takeIf { it in profile.subscriptionUrls }?.let { return it }
        val host = metadata.sourceHost?.lowercase() ?: return null
        return profile.subscriptionUrls.firstOrNull { hostOf(it) == host }
    }

    fun remove(profile: ClientProfile, url: String): ClientProfile {
        if (url !in profile.subscriptionUrls) return profile
        val selectedRaw = profile.selectedNode?.raw
        val kept = profile.nodes.filterNot { SubscriptionMembership.belongs(it, url, profile.subscriptionUrls) }
        val index = kept.indexOfFirst { it.raw == selectedRaw }.takeIf { it >= 0 } ?: 0
        return profile.copy(nodes = kept, selected = index, subscriptionUrls = profile.subscriptionUrls - url)
    }
}
