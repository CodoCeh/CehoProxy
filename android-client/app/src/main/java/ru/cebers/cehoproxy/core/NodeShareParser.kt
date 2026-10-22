package ru.cebers.cehoproxy.core

import java.net.URI
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import org.json.JSONArray
import org.json.JSONObject

data class ProxyNode(
    val name: String,
    val type: String,
    val server: String,
    val port: Int,
    val raw: String,
    val subscriptionExpiresAt: Long? = null,
    val subscriptionMetadata: SubscriptionMetadata = SubscriptionMetadata(expiresAtEpochSeconds = subscriptionExpiresAt),
) {
    val routeId: String get() = java.security.MessageDigest.getInstance("SHA-256")
        .digest(raw.toByteArray(StandardCharsets.UTF_8)).joinToString("") { "%02x".format(it) }
}

object NodeShareParser {
    private val supported = setOf("vless", "vmess", "trojan", "ss", "hysteria2", "hy2", "tuic", "naive", "socks", "socks4", "socks4a", "socks5", "socks5h", "http")
    private val supportedOutbounds = supported + setOf("shadowsocks", "naiveproxy", "anytls", "hysteria", "ssh", "wireguard")
    private val unsupportedLinkSchemes = setOf("ssr", "juicity", "mieru", "brook", "snell", "trojan-go", "mtproto", "tg")
    private val nonProxyOutbounds = setOf("direct", "block", "dns", "selector", "urltest", "loadbalance")

    fun parse(input: String): List<ProxyNode> {
        val decoded = decodeSubscription(input.trim())
        val formatted = DesktopSubscriptionFormatParser.parse(decoded)
        if (formatted.isNotEmpty()) return formatted
        val jsonNodes = parseJsonNodes(decoded)
        if (jsonNodes.isNotEmpty()) return jsonNodes
        val wireguardNodes = ExtraProtocols.parseWireguardConfig(decoded)
        if (wireguardNodes.isNotEmpty()) return wireguardNodes
        return decoded.lineSequence().mapNotNullIndexed { index, line -> parseLine(line.trim(), index) }.toList()
    }

    fun isHttpProxyUri(value: String): Boolean = runCatching {
        val uri = URI(value.trim())
        uri.scheme.equals("http", true) && !uri.host.isNullOrBlank() && uri.port in 1..65535 &&
            (uri.rawPath.isNullOrEmpty() || uri.rawPath == "/") && uri.rawQuery.isNullOrEmpty()
    }.getOrDefault(false)

    fun unsupportedTypes(input: String): List<String> {
        val text = decodeSubscription(input.trim())
        val found = linkedSetOf<String>()
        runCatching {
            val candidates = when {
                text.startsWith("[") -> JSONArray(text).let { array -> (0 until array.length()).mapNotNull(array::optJSONObject) }
                text.startsWith("{") -> JSONObject(text).let { root ->
                    val outbounds = root.optJSONArray("outbounds")
                    if (outbounds != null) (0 until outbounds.length()).mapNotNull(outbounds::optJSONObject)
                    else listOf(root)
                }
                else -> emptyList()
            }
            candidates.mapNotNull { it.optString("type").ifBlank { it.optString("protocol") }.lowercase().takeIf(String::isNotBlank) }
                .filterNot { it in supportedOutbounds || it in nonProxyOutbounds }.forEach(found::add)
        }
        text.lineSequence().map { it.trim().substringBefore("://", "").lowercase() }.filter { it in unsupportedLinkSchemes }.forEach(found::add)
        if (text.contains("proxies:")) {
            var inProxies = false
            text.lineSequence().forEach { line ->
                if (line.trim() == "proxies:") { inProxies = true; return@forEach }
                if (inProxies && line.isNotBlank() && !line.first().isWhitespace()) inProxies = false
                if (inProxies) Regex("^\\s*type:\\s*['\\\"]?([A-Za-z0-9_-]+)").find(line)?.groupValues?.getOrNull(1)?.lowercase()
                    ?.let { if (it !in supportedOutbounds && it !in nonProxyOutbounds) found += it }
            }
        }
        return found.toList()
    }

    private fun parseJsonNodes(text: String): List<ProxyNode> = runCatching {
        val json = JSONObject(text)
        val outbounds = json.optJSONArray("outbounds") ?: JSONArray()
        val result = mutableListOf<ProxyNode>()
        val endpoints = json.optJSONArray("endpoints")
        if (endpoints != null) for (index in 0 until endpoints.length()) {
            val endpoint = endpoints.optJSONObject(index)?.takeIf { it.optString("type") == "wireguard" } ?: continue
            ExtraProtocols.wireguardEndpoint(endpoint)?.let { result += ExtraProtocols.nodeOf(it) }
        }
        for (index in 0 until outbounds.length()) {
            val outbound = outbounds.optJSONObject(index) ?: continue
            val type = outbound.optString("type")
            if (type == "wireguard") { ExtraProtocols.wireguardEndpoint(outbound)?.let { result += ExtraProtocols.nodeOf(it) }; continue }
            if (type !in setOf("vless", "vmess", "trojan", "shadowsocks", "hysteria2", "hysteria", "anytls", "ssh", "tuic", "naive", "http", "socks")) continue
            val host = outbound.optString("server").takeIf(String::isNotBlank) ?: continue
            val port = outbound.optInt("server_port").takeIf { it in 1..65535 } ?: continue
            result += ProxyNode(outbound.optString("tag", "$type $host"), type, host, port, outbound.toString())
        }
        result
    }.getOrDefault(emptyList())

    fun decodeSubscription(input: String): String {
        if (input.contains("://")) return input
        val compact = input.filterNot(Char::isWhitespace).replace('-', '+').replace('_', '/')
        return runCatching { String(Base64Compat.decode(compact), StandardCharsets.UTF_8) }.getOrDefault(input)
    }

    private fun parseLine(line: String, index: Int): ProxyNode? {
        if (line.isBlank() || line.startsWith("{")) return null
        if (line.substringBefore("://", "").lowercase() in ExtraProtocols.linkSchemes) return ExtraProtocols.parseLink(line)
        val normalizedNaive = normalizeNaiveUri(line) ?: line
        val uri = runCatching { URI(normalizedNaive) }.getOrNull() ?: return null
        val type = uri.scheme?.lowercase() ?: return null
        if (type !in supported && type != "https" && type != "http2" && type != "quic") return null
        val effectiveType = if (normalizedNaive != line) "naive" else type
        if (effectiveType !in supported) return null
        if (effectiveType == "vmess") return parseVmess(line)
        if (effectiveType == "ss" && !line.substringAfter("ss://").substringBefore('#').contains('@')) return parseLegacyShadowsocks(line)
        val host = uri.host ?: return null
        val port = uri.port
        val effectivePort = if (port == -1 && effectiveType == "naive") if (uri.scheme == "http2") 80 else 443 else port
        if (effectivePort !in 1..65535) return null
        val label = uri.rawFragment?.let { URLDecoder.decode(it, StandardCharsets.UTF_8.name()) }?.takeIf(String::isNotBlank) ?: "$effectiveType • $host"
        return ProxyNode(label, if (effectiveType == "hy2") "hysteria2" else effectiveType, host, effectivePort, line)
    }

    private fun normalizeNaiveUri(line: String): String? {
        val lower = line.lowercase()
        val body = when {
            lower.startsWith("naive+https://") -> line.substringAfter("://").let { "naive://$it" }
            lower.startsWith("naive+quic://") -> line.substringAfter("://").let { "naive://$it" }
            lower.startsWith("naive://") -> line
            lower.startsWith("https://") || lower.startsWith("http2://") || lower.startsWith("quic://") -> {
                val rest = line.substringAfter("://")
                val authority = rest.substringBefore('/').substringBefore('?').substringBefore('#')
                if ('@' !in authority) return null
                "naive://$rest"
            }
            else -> return null
        }
        val uri = runCatching { URI(body) }.getOrNull() ?: return null
        if (uri.rawUserInfo.isNullOrBlank() || uri.host.isNullOrBlank()) return null
        val p = uri.port
        if (p != -1 && p !in 1..65535) return null
        return body
    }

    private fun parseVmess(line: String): ProxyNode? = runCatching {
        val encoded = line.substringAfter("vmess://").substringBefore('#')
        val json = JSONObject(String(Base64Compat.decode(encoded), StandardCharsets.UTF_8))
        val host = json.getString("add")
        val port = json.getInt("port")
        ProxyNode(json.optString("ps", "vmess • $host"), "vmess", host, port, line)
    }.getOrNull()

    private fun parseLegacyShadowsocks(line: String): ProxyNode? = runCatching {
        val payload = line.substringAfter("ss://").substringBefore('#').substringBefore('?')
        val decoded = String(Base64Compat.decode(payload.replace('-', '+').replace('_', '/')), StandardCharsets.UTF_8)
        val at = decoded.lastIndexOf('@')
        require(at > 0)
        val address = URI("socks5://${decoded.substring(at + 1)}")
        ProxyNode("ss • ${address.host}", "ss", address.host, address.port, "ss://${payload}")
    }.getOrNull()

    private fun <T, R : Any> Sequence<T>.mapNotNullIndexed(transform: (Int, T) -> R?): Sequence<R> = sequence {
        for ((index, item) in withIndex()) transform(index + 1, item)?.let { yield(it) }
    }
}
