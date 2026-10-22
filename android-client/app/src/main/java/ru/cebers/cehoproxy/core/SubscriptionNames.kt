package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject

/**
 * Some providers name every node "proxy" for the native sing-box client and send readable names to other clients.
 * The native body keeps the config we can run; names come from a second request and are matched by host:port.
 */
object SubscriptionNames {
    // The provider switches to the named format for any User-Agent without the "sing-box" marker.
    const val NAMES_USER_AGENT = "CehoProxyAndroid/0.1"

    fun isGeneric(name: String): Boolean = name.trim().let { it.isEmpty() || it.equals("proxy", ignoreCase = true) }

    fun fromBody(text: String): Map<String, String> = runCatching {
        val containers = if (text.trimStart().startsWith("[")) JSONArray(text).let { array -> (0 until array.length()).mapNotNull(array::optJSONObject) }
        else listOf(JSONObject(text))
        val names = linkedMapOf<String, MutableList<String>>()
        for (container in containers) {
            val outbounds = container.optJSONArray("outbounds") ?: continue
            for (index in 0 until outbounds.length()) {
                val outbound = outbounds.optJSONObject(index) ?: continue
                val tag = outbound.optString("tag").takeIf { it.isNotBlank() && it !in setOf("direct", "block", "dns") } ?: continue
                val settings = outbound.optJSONObject("settings")
                val vnext = settings?.optJSONArray("vnext")?.optJSONObject(0)
                val host = outbound.optString("server").takeIf(String::isNotBlank) ?: vnext?.optString("address") ?: settings?.optString("address")
                val port = outbound.optInt("server_port").takeIf { it > 0 } ?: vnext?.optInt("port") ?: settings?.optInt("port")
                if (host.isNullOrBlank() || port == null || port !in 1..65535) continue
                names.getOrPut("$host:$port") { mutableListOf() }.add(tag)
            }
        }
        // Several tags on one address mean we cannot tell which name belongs to which node; leave those unnamed.
        names.filterValues { it.distinct().size == 1 }.mapValues { it.value.first() }
    }.getOrDefault(emptyMap())

    fun apply(nodes: List<ProxyNode>, names: Map<String, String>): List<ProxyNode> = nodes.map { node ->
        if (!isGeneric(NodePresentation.displayName(node.name))) node
        else names["${node.server}:${node.port}"]?.let { node.copy(name = it) } ?: node
    }

    /**
     * Nodes for the native client, with readable names when the provider gave generic ones.
     * If the named format lists more nodes, they are added to the native ones (the native body may carry only one).
     * With [required] a failed second request throws, so a refresh keeps the saved list instead of shrinking it to the native nodes.
     */
    fun resolve(url: String, nodes: List<ProxyNode>, required: Boolean = false, fetch: (String, SubscriptionRequestOptions) -> String = SubscriptionFetcher::fetch): List<ProxyNode> {
        if (nodes.none { isGeneric(NodePresentation.displayName(it.name)) }) return nodes
        val body = if (required) fetch(url, SubscriptionRequestOptions(NAMES_USER_AGENT))
        else runCatching { fetch(url, SubscriptionRequestOptions(NAMES_USER_AGENT)) }.getOrNull() ?: return nodes
        val renamed = apply(nodes, fromBody(body))
        val named = runCatching { NodeShareParser.parse(body) }.getOrDefault(emptyList())
        if (named.size <= nodes.size) return renamed
        // Native nodes with their own account (not in the named list) stay, e.g. the main USA node.
        val namedRaws = named.map { it.raw }.toSet()
        return named + renamed.filterNot { it.raw in namedRaws }
    }
}
