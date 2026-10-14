package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject
import java.net.URI
import java.net.URLDecoder

/** Runs VLESS nodes on transports sing-box cannot run (xhttp, mKCP) through a local Xray-core process. */
internal object XrayBridge {
    private val transports = setOf("xhttp", "splithttp", "kcp", "mkcp")

    fun needsXray(node: ProxyNode): Boolean = node.type == "vless" && networkOf(node) in transports

    fun config(nodes: List<ProxyNode>, socksPorts: Map<String, Int>): String {
        val inbounds = JSONArray()
        val outbounds = JSONArray()
        val rules = JSONArray()
        nodes.forEachIndexed { index, node ->
            val port = requireNotNull(socksPorts[node.routeId]) { "Нет локального порта для узла ${node.name}" }
            val inTag = "in-$index"
            val outTag = "out-$index"
            inbounds.put(JSONObject().put("tag", inTag).put("listen", "127.0.0.1").put("port", port).put("protocol", "socks")
                .put("settings", JSONObject().put("auth", "noauth").put("udp", true)))
            outbounds.put(vlessOutbound(node, outTag))
            rules.put(JSONObject().put("type", "field").put("inboundTag", JSONArray().put(inTag)).put("outboundTag", outTag))
        }
        return JSONObject()
            .put("log", JSONObject().put("loglevel", "warning"))
            .put("inbounds", inbounds)
            .put("outbounds", outbounds)
            .put("routing", JSONObject().put("rules", rules))
            .toString()
    }

    private fun vlessOutbound(node: ProxyNode, tag: String): JSONObject {
        val uri = URI(node.raw)
        val query = query(uri)
        val user = JSONObject().put("id", decode(uri.rawUserInfo.orEmpty())).put("encryption", query["encryption"].orEmpty().ifBlank { "none" })
        putIfNotBlank(user, "flow", query["flow"])
        val stream = JSONObject().put("network", networkOf(node))
        when (query["security"].orEmpty().lowercase()) {
            "reality" -> stream.put("security", "reality").put("realitySettings", JSONObject().apply {
                put("serverName", query["sni"].orEmpty())
                put("publicKey", query["pbk"].orEmpty())
                putIfNotBlank(this, "shortId", query["sid"])
                put("fingerprint", query["fp"].orEmpty().ifBlank { "chrome" })
                putIfNotBlank(this, "spiderX", query["spx"])
            })
            "tls" -> stream.put("security", "tls").put("tlsSettings", JSONObject().apply {
                put("serverName", query["sni"].orEmpty())
                put("allowInsecure", query["allowInsecure"] == "1" || query["insecure"] == "1")
                put("fingerprint", query["fp"].orEmpty().ifBlank { "chrome" })
                query["alpn"]?.split(',')?.map(String::trim)?.filter(String::isNotBlank)?.takeIf { it.isNotEmpty() }
                    ?.let { put("alpn", JSONArray(it)) }
            })
            else -> stream.put("security", "none")
        }
        when (networkOf(node)) {
            "xhttp", "splithttp" -> stream.put("xhttpSettings", JSONObject().apply {
                put("mode", query["mode"].orEmpty().ifBlank { "auto" })
                put("path", query["path"].orEmpty().ifBlank { "/" })
                putIfNotBlank(this, "host", query["host"])
            })
            "kcp", "mkcp" -> stream.put("kcpSettings", JSONObject().apply {
                put("header", JSONObject().put("type", query["headerType"].orEmpty().ifBlank { "none" }))
                putIfNotBlank(this, "seed", query["seed"])
            })
        }
        val address = JSONObject().put("address", uri.host).put("port", uri.port).put("users", JSONArray().put(user))
        return JSONObject()
            .put("tag", tag)
            .put("protocol", "vless")
            .put("settings", JSONObject().put("vnext", JSONArray().put(address)))
            .put("streamSettings", stream)
    }

    private fun networkOf(node: ProxyNode): String = query(URI(node.raw))["type"].orEmpty().ifBlank { "tcp" }.lowercase()

    private fun query(uri: URI): Map<String, String> = uri.rawQuery.orEmpty().split('&').mapNotNull { entry ->
        val split = entry.indexOf('=')
        if (split < 0) null else decode(entry.substring(0, split)) to decode(entry.substring(split + 1))
    }.toMap()

    private fun decode(value: String): String = URLDecoder.decode(value.replace("+", "%2B"), Charsets.UTF_8.name())

    private fun putIfNotBlank(target: JSONObject, key: String, value: String?) {
        if (!value.isNullOrBlank()) target.put(key, value)
    }
}
