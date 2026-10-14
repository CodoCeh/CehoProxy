package ru.cebers.cehoproxy.core

/**
 * Which subscription URL a node came from. Nodes saved before sourceUrl existed match by host,
 * and only when no other subscription in the profile shares that host.
 */
internal object SubscriptionMembership {
    fun belongs(node: ProxyNode, url: String, subscriptionUrls: List<String>): Boolean {
        node.subscriptionMetadata.sourceUrl?.let { return it == url }
        val host = SubscriptionRemovalPolicy.hostOf(url) ?: return false
        if (node.subscriptionMetadata.sourceHost?.lowercase() != host) return false
        return subscriptionUrls.count { SubscriptionRemovalPolicy.hostOf(it) == host } == 1
    }
}
