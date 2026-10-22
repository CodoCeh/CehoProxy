package ru.cebers.cehoproxy.core

import java.net.URI

enum class SubscriptionInputKind { DIRECT_NODE, REMOTE_SUBSCRIPTION, TEXT }

object SubscriptionInput {
    private val nodeSchemes = setOf(
        "http", "https", "vless", "vmess", "trojan", "ss", "ssr", "hysteria", "hysteria2", "hy", "hy2", "tuic", "naive", "naive+https", "naive+quic",
        "http2", "quic", "socks", "socks4", "socks4a", "socks5", "socks5h", "anytls", "wireguard", "wg", "ssh",
    )

    /** Providers hand out "add to app" links that wrap the real subscription address; this returns the address inside, or the input unchanged. */
    fun unwrap(value: String): String {
        val text = value.trim()
        if (text.isEmpty() || text.any { it == '\n' || it == '\r' }) return value
        val scheme = text.substringBefore("://", "").lowercase()
        if (scheme.isEmpty() || scheme in nodeSchemes) return value
        if (scheme == "happ" && text.substringAfter("://").startsWith("crypt", ignoreCase = true)) return value
        val inQuery = text.substringAfter('?', "").substringBefore('#').split('&').firstNotNullOfOrNull { pair ->
            if (pair.substringBefore('=').equals("url", true)) decode(pair.substringAfter('=', "")) else null
        }
        if (inQuery != null && isWebAddress(inQuery)) return inQuery
        val rest = text.substringAfter("://").trimStart('/').substringBefore('#')
        val tail = rest.substringAfter('/', "").takeIf { rest.substringBefore('/').lowercase() in setOf("import", "add", "sub", "install-sub", "install-config", "import-remote-profile") } ?: rest
        val body = tail.removePrefix("sub://")
        val decoded = decode(body)
        if (isWebAddress(decoded)) return decoded
        runCatching { String(Base64Compat.decode(body.replace('-', '+').replace('_', '/')), Charsets.UTF_8) }.getOrNull()
            ?.takeIf(::isWebAddress)?.let { return it }
        return value
    }

    private fun isWebAddress(value: String) = value.startsWith("https://", true) || value.startsWith("http://", true)
    private fun decode(value: String) = runCatching { java.net.URLDecoder.decode(value.replace("+", "%2B"), "UTF-8") }.getOrDefault(value)

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
