package ru.cebers.cehoproxy.core

/** Finds a subscription link or server in copied text that the profile does not have yet. */
internal object ClipboardOfferPolicy {
    private val subscriptionSegment = Regex("(?i)^(sub|subs|subscription|subscriptions|getsub|link|links|s|client|config|configs)$")
    private val tokenSegment = Regex("^[A-Za-z0-9_-]{16,}$")
    private const val MAX_LENGTH = 8_000

    fun candidate(text: String?, profile: ClientProfile): String? {
        val trimmed = text?.trim().orEmpty()
        if (trimmed.isEmpty() || trimmed.length > MAX_LENGTH) return null
        val unwrapped = SubscriptionInput.unwrap(trimmed).trim()
        val scheme = unwrapped.substringBefore("://", "").lowercase()
        val offer = when {
            scheme == "http" || scheme == "https" ->
                unwrapped.takeIf { unwrapped != trimmed || NodeShareParser.isHttpProxyUri(unwrapped) || looksLikeSubscription(unwrapped) }
            runCatching { NodeShareParser.parse(trimmed) }.getOrDefault(emptyList()).isNotEmpty() -> trimmed
            else -> null
        } ?: return null
        val key = comparable(offer)
        if (profile.subscriptionUrls.any { comparable(it) == key } || profile.nodes.any { comparable(it.raw) == key }) return null
        return offer
    }

    /** Copies of the same link differ in a fragment, a trailing slash or the case of the scheme and host. */
    private fun comparable(link: String): String {
        val bare = link.trim().substringBefore('#').trimEnd('/')
        val scheme = bare.substringBefore("://", "")
        if (scheme.isEmpty()) return bare
        val rest = bare.substringAfter("://")
        val authority = rest.substringBefore('/')
        return scheme.lowercase() + "://" + authority.lowercase() + rest.removePrefix(authority)
    }

    private fun looksLikeSubscription(url: String): Boolean {
        val uri = runCatching { java.net.URI(url) }.getOrNull() ?: return false
        val host = uri.host?.lowercase() ?: return false
        val segments = uri.rawPath.orEmpty().split('/').filter { it.isNotEmpty() }
        val query = uri.rawQuery.orEmpty().lowercase()
        return host.startsWith("sub.") || host.startsWith("subs.") ||
            segments.any { subscriptionSegment.matches(it) || tokenSegment.matches(it) } ||
            query.split('&').any { it.substringBefore('=') in setOf("token", "sub", "subscription", "key") }
    }
}
