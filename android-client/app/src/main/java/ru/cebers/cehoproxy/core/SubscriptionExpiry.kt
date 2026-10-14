package ru.cebers.cehoproxy.core

internal object SubscriptionExpiry {
    /** True when every node has a known expiry in the past. Unknown expiry counts as active. */
    fun allExpired(nodes: List<ProxyNode>, nowEpochSeconds: Long): Boolean {
        if (nodes.isEmpty()) return false
        return nodes.all { node ->
            val expiresAt = node.subscriptionExpiresAt ?: node.subscriptionMetadata.expiresAtEpochSeconds
            expiresAt != null && expiresAt > 0 && expiresAt <= nowEpochSeconds
        }
    }
}
