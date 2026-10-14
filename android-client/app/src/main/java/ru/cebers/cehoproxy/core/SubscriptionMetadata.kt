package ru.cebers.cehoproxy.core

import java.net.HttpURLConnection
import java.net.URI

data class SubscriptionMetadata(
    val expiresAtEpochSeconds: Long? = null,
    val uploadBytes: Long? = null,
    val downloadBytes: Long? = null,
    val totalBytes: Long? = null,
    val title: String? = null,
    val infoText: String? = null,
    val announcement: String? = null,
    val supportUrl: String? = null,
    val webPageUrl: String? = null,
    val infoButtonText: String? = null,
    val infoButtonUrl: String? = null,
    val renewUrl: String? = null,
    val notifyOnExpiry: Boolean = false,
    val sourceHost: String? = null,
    val sourceUrl: String? = null,
) {
    val usedBytes: Long? get() = if (uploadBytes != null && downloadBytes != null) safeSum(uploadBytes, downloadBytes) else uploadBytes ?: downloadBytes

    fun attachTo(nodes: List<ProxyNode>): List<ProxyNode> = nodes.map { node ->
        val merged = mergeMissing(node.subscriptionMetadata)
        node.copy(subscriptionExpiresAt = expiresAtEpochSeconds ?: node.subscriptionExpiresAt ?: merged.expiresAtEpochSeconds, subscriptionMetadata = merged)
    }

    fun mergeMissing(other: SubscriptionMetadata): SubscriptionMetadata = SubscriptionMetadata(
        expiresAtEpochSeconds = expiresAtEpochSeconds ?: other.expiresAtEpochSeconds,
        uploadBytes = uploadBytes ?: other.uploadBytes,
        downloadBytes = downloadBytes ?: other.downloadBytes,
        totalBytes = totalBytes ?: other.totalBytes,
        title = title ?: other.title,
        infoText = infoText ?: other.infoText,
        announcement = announcement ?: other.announcement,
        supportUrl = supportUrl ?: other.supportUrl,
        webPageUrl = webPageUrl ?: other.webPageUrl,
        infoButtonText = infoButtonText ?: other.infoButtonText,
        infoButtonUrl = infoButtonUrl ?: other.infoButtonUrl,
        renewUrl = renewUrl ?: other.renewUrl,
        notifyOnExpiry = notifyOnExpiry || other.notifyOnExpiry,
        sourceHost = sourceHost ?: other.sourceHost,
        sourceUrl = sourceUrl ?: other.sourceUrl,
    )

    companion object {
        private val supportedKeys = setOf(
            "subscription-userinfo", "profile-userinfo", "profile-title", "sub-info-text", "announce",
            "support-url", "profile-web-page-url", "sub-info-button-text", "sub-info-button-link",
            "sub-expire-button-link", "notification-subs-expire", "sub-expire",
        )

        fun from(connection: HttpURLConnection, body: String): SubscriptionMetadata {
            val values = linkedMapOf<String, String>()
            connection.headerFields.forEach { (key, entries) ->
                key?.lowercase()?.takeIf { it in supportedKeys }?.let { name -> entries?.firstOrNull()?.let { values[name] = it } }
            }
            return fromValues(values + bodyValues(body).filterKeys { it !in values })
        }

        fun fromBody(body: String): SubscriptionMetadata = fromValues(bodyValues(body))

        private fun bodyValues(body: String): Map<String, String> {
            val values = linkedMapOf<String, String>()
            NodeShareParser.decodeSubscription(body).lineSequence().take(500).forEach { line ->
                val match = Regex("^\\s*#\\s*([A-Za-z0-9-]+)\\s*:\\s*(.*?)\\s*$").matchEntire(line) ?: return@forEach
                val key = match.groupValues[1].lowercase()
                if (key in supportedKeys && key !in values) values[key] = match.groupValues[2]
            }
            return values
        }

        fun fromHeader(header: String?): SubscriptionMetadata = fromValues(mapOf("subscription-userinfo" to header.orEmpty()))

        fun fromValues(values: Map<String, String>): SubscriptionMetadata {
            val normalized = values.mapKeys { it.key.lowercase() }
            val userInfo = normalized["subscription-userinfo"] ?: normalized["profile-userinfo"].orEmpty()
            val counters = userInfo.split(';', ',').mapNotNull { part ->
                val pair = part.split('=', limit = 2)
                if (pair.size == 2) pair[0].trim().lowercase() to pair[1].trim().toLongOrNull()?.takeIf { it >= 0 } else null
            }.toMap()
            fun text(key: String, maxLength: Int = 512): String? = normalized[key]?.trim()?.takeIf { it.isNotEmpty() && it != "0" }?.take(maxLength)
            fun title(): String? {
                val value = text("profile-title", 4096) ?: return null
                val decoded = if (value.startsWith("base64:", true)) runCatching {
                    val encoded = value.substringAfter(':').filterNot(Char::isWhitespace)
                    String(java.util.Base64.getDecoder().decode(encoded), Charsets.UTF_8).trim()
                }.getOrNull() ?: return null else value
                return decoded.takeIf(String::isNotBlank)?.take(80)
            }
            fun link(key: String): String? = text(key)?.takeIf(::isSafeLink)
            fun enabled(key: String): Boolean = normalized[key]?.trim()?.lowercase() in setOf("1", "true", "yes", "on")
            val expire = counters["expire"]?.takeIf { it in 1..253_402_300_799L }
            return SubscriptionMetadata(
                expiresAtEpochSeconds = expire,
                uploadBytes = counters["upload"], downloadBytes = counters["download"], totalBytes = counters["total"],
                title = title(), infoText = text("sub-info-text", 200), announcement = text("announce", 200),
                supportUrl = link("support-url"), webPageUrl = link("profile-web-page-url"),
                infoButtonText = text("sub-info-button-text", 25), infoButtonUrl = link("sub-info-button-link"),
                renewUrl = link("sub-expire-button-link"),
                notifyOnExpiry = enabled("notification-subs-expire") || enabled("sub-expire"),
            )
        }

        private fun isSafeLink(value: String): Boolean = runCatching {
            val uri = URI(value)
            when (uri.scheme?.lowercase()) {
                "https" -> !uri.host.isNullOrBlank()
                "http" -> !uri.host.isNullOrBlank()
                "tg" -> uri.host.equals("resolve", true) || uri.host.equals("join", true)
                else -> false
            }
        }.getOrDefault(false)

        private fun safeSum(a: Long, b: Long): Long = if (Long.MAX_VALUE - a < b) Long.MAX_VALUE else a + b
    }
}
