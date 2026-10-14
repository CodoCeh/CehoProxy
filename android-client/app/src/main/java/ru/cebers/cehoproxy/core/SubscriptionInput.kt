package ru.cebers.cehoproxy.core

import java.net.URI

enum class SubscriptionInputKind { DIRECT_NODE, REMOTE_SUBSCRIPTION, TEXT }

object SubscriptionInput {
    fun classify(value: String): SubscriptionInputKind {
        val text = value.trim()
        val uri = runCatching { URI(text) }.getOrNull()
        val scheme = uri?.scheme?.lowercase()
        if (NodeShareParser.isHttpProxyUri(text)) return SubscriptionInputKind.DIRECT_NODE
        if (scheme == "https" && !uri.rawUserInfo.isNullOrBlank() && !uri.host.isNullOrBlank() &&
            NodeShareParser.parse(text).any { it.type == "naive" }
        ) return SubscriptionInputKind.DIRECT_NODE
        return if (scheme == "http" || scheme == "https") SubscriptionInputKind.REMOTE_SUBSCRIPTION
        else SubscriptionInputKind.TEXT
    }
}
