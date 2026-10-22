package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject

/** Imports common desktop subscription containers into sing-box outbounds understood by Android. */
object DesktopSubscriptionFormatParser {
    private val knownTypes = setOf("vless", "vmess", "trojan", "shadowsocks", "hysteria2", "hysteria", "anytls", "ssh", "tuic", "naive", "http", "socks")

    fun parse(text: String): List<ProxyNode> {
        val source = text.trimStart('\uFEFF', ' ', '\t', '\r', '\n')
        if (source.startsWith("{") || source.startsWith("[")) return parseJson(source)
        if (source.contains("proxies:")) return parseClash(source)
        return emptyList()
    }

    private fun parseJson(text: String): List<ProxyNode> = runCatching {
        val result = mutableListOf<ProxyNode>()
        when {
            text.startsWith("[") -> JSONArray(text).let { array ->
                for (i in 0 until array.length()) array.optJSONObject(i)?.let { readContainer(it, result) }
            }
            else -> readContainer(JSONObject(text), result)
        }
        result
    }.getOrElse { error ->
        emptyList()
    }

    private fun readContainer(obj: JSONObject, result: MutableList<ProxyNode>) {
        val remarks = obj.optString("remarks").ifBlank { obj.optString("name") }
        if (appendOutbound(obj, result, remarks)) return
        val outbounds = obj.optJSONArray("outbounds")
        if (outbounds != null) {
            val proxyCount = (0 until outbounds.length()).count { outbounds.optJSONObject(it)?.let { o -> (o.string("type") ?: o.string("protocol"))?.lowercase() } in knownTypes }
            val containerName = if (proxyCount == 1) remarks else ""
            for (i in 0 until outbounds.length()) outbounds.optJSONObject(i)?.let { appendOutbound(it, result, containerName) }
        }
        val endpoints = obj.optJSONArray("endpoints")
        if (endpoints != null) for (i in 0 until endpoints.length()) endpoints.optJSONObject(i)?.takeIf { it.string("type") == "wireguard" }
            ?.let { ExtraProtocols.wireguardEndpoint(it) }?.let { result += ExtraProtocols.nodeOf(it) }
        val servers = obj.optJSONArray("servers")
        if (servers != null) for (i in 0 until servers.length()) servers.optJSONObject(i)?.let { server ->
            val host = server.string("server") ?: return@let
            val port = server.integer("server_port") ?: return@let
            val raw = JSONObject().put("type", "shadowsocks").put("tag", server.string("remarks") ?: server.string("name") ?: "ss • $host")
                .put("server", host).put("server_port", port).put("method", server.string("method") ?: "")
                .put("password", server.string("password") ?: "")
            result += ProxyNode(raw.getString("tag"), "shadowsocks", host, port, raw.toString())
        }
    }

    private fun appendOutbound(obj: JSONObject, into: MutableList<ProxyNode>, containerName: String = ""): Boolean {
        val existingType = obj.string("type")?.lowercase()
        if (existingType == "wireguard") {
            val endpoint = ExtraProtocols.wireguardEndpoint(obj) ?: return false
            into += ExtraProtocols.nodeOf(endpoint, containerName.ifBlank { null })
            return true
        }
        if (existingType != null) {
            if (existingType !in knownTypes) return false
            val host = obj.string("server") ?: return false
            val port = obj.integer("server_port")?.takeIf { it in 1..65535 } ?: return false
            val copy = JSONObject(obj.toString())
            val tag = copy.string("tag") ?: "$existingType • $host"
            copy.put("tag", tag)
            into += ProxyNode(containerName.ifBlank { tag }, existingType, host, port, copy.toString())
            return true
        }
        val protocol = obj.string("protocol")?.lowercase() ?: return false
        val settings = obj.optJSONObject("settings") ?: return false
        if (protocol == "wireguard") return xrayWireguard(obj, settings, into, containerName)
        if (protocol == "hysteria" && settings.optInt("version") == 1) return false
        val type = when (protocol) {
            "ss", "shadowsocks" -> "shadowsocks"
            "hy2", "hysteria", "hysteria2" -> "hysteria2"
            "naiveproxy" -> "naive"
            else -> protocol
        }
        if (type !in knownTypes) return false
        val vnext = settings.optJSONArray("vnext")?.optJSONObject(0)
        val serverEntry = settings.optJSONArray("servers")?.optJSONObject(0)
        val host = vnext?.string("address") ?: serverEntry?.string("address") ?: settings.string("address") ?: return false
        val port = vnext?.integer("port") ?: serverEntry?.integer("port") ?: settings.integer("port") ?: return false
        if (port !in 1..65535) return false
        val user = vnext?.optJSONArray("users")?.optJSONObject(0)
        val outbound = JSONObject().put("type", type).put("server", host).put("server_port", port)
        val tag = containerName.ifBlank { obj.string("tag") ?: "$type • $host" }
        outbound.put("tag", tag)
        val stream = obj.optJSONObject("streamSettings")
        val credential = user?.string("id") ?: serverEntry?.string("password") ?: settings.string("password") ?: settings.string("auth")
        when (type) {
            "vless", "vmess", "tuic" -> credential?.let { outbound.put("uuid", it) }
            "trojan", "hysteria2", "shadowsocks" -> credential?.let { outbound.put("password", it) }
            "naive" -> {
                outbound.put("username", settings.string("username") ?: "")
                outbound.put("password", settings.string("password") ?: "")
            }
            "http", "socks" -> {
                settings.string("username")?.let { outbound.put("username", it) }
                settings.string("password")?.let { outbound.put("password", it) }
            }
        }
        if (type == "shadowsocks") outbound.put("method", serverEntry?.string("method") ?: user?.string("security") ?: settings.string("method") ?: "")
        if (type == "vmess") {
            outbound.put("alter_id", user?.optInt("alterId", 0) ?: 0)
            outbound.put("security", user?.string("security") ?: "auto")
        }
        if (type == "vless") user?.string("flow")?.let { outbound.put("flow", it) }
        if (type == "tuic") serverEntry?.string("password")?.let { outbound.put("password", it) }
        if (type == "hysteria2") {
            val hysteria = stream?.optJSONObject("hysteriaSettings")
            (hysteria?.string("auth") ?: hysteria?.string("password"))?.let { outbound.put("password", it) }
            (hysteria?.string("obfsPassword") ?: hysteria?.string("obfs"))?.takeIf { it.isNotBlank() && it != "plain" }
                ?.let { outbound.put("obfs", JSONObject().put("type", "salamander").put("password", it)) }
        }
        val security = stream?.string("security")?.lowercase().orEmpty()
        if (security == "tls" || security == "reality" || type in setOf("trojan", "hysteria2", "tuic", "naive")) {
            val tlsSettings = stream?.optJSONObject("tlsSettings")
            val certificatePin = tlsSettings?.opt("pinnedPeerCertSha256")
            val hasCertificatePin = when (certificatePin) {
                null, JSONObject.NULL -> false
                is JSONArray -> certificatePin.length() > 0
                is String -> certificatePin.isNotBlank()
                else -> true
            }
            val realitySettings = stream?.optJSONObject("realitySettings")
            val tls = JSONObject().put("enabled", true)
            // Provider pins the whole certificate; sing-box cannot check that hash, so the node is accepted without verification.
            if (hasCertificatePin) tls.put("insecure", true)
            (realitySettings?.string("serverName") ?: tlsSettings?.string("serverName"))?.let { tls.put("server_name", it) }
            if (tlsSettings?.optBoolean("allowInsecure") == true) tls.put("insecure", true)
            val fingerprint = realitySettings?.string("fingerprint") ?: tlsSettings?.string("fingerprint")
                ?: if (security == "reality") "chrome" else null
            fingerprint?.let { tls.put("utls", JSONObject().put("enabled", true).put("fingerprint", it)) }
            if (security == "reality") {
                val reality = JSONObject().put("enabled", true)
                realitySettings?.string("publicKey")?.let { reality.put("public_key", it) }
                realitySettings?.string("shortId")?.let { reality.put("short_id", it) }
                tls.put("reality", reality)
            }
            outbound.put("tls", tls)
        }
        stream?.optJSONObject("wsSettings")?.let { ws ->
            val transport = JSONObject().put("type", "ws")
            ws.string("path")?.let { transport.put("path", it) }
            ws.optJSONObject("headers")?.string("Host")?.let { transport.put("headers", JSONObject().put("Host", it)) }
            outbound.put("transport", transport)
        }
        stream?.optJSONObject("grpcSettings")?.let { grpc -> outbound.put("transport", JSONObject().put("type", "grpc").put("service_name", grpc.string("serviceName") ?: "")) }
        stream?.optJSONObject("httpSettings")?.let { http ->
            val transport = JSONObject().put("type", "http")
            http.string("path")?.let { transport.put("path", it) }
            val hosts = http.optJSONArray("host")
            if (hosts != null) transport.put("host", hosts) else http.string("host")?.let { transport.put("host", JSONArray().put(it)) }
            outbound.put("transport", transport)
        }
        val raw = outbound.toString()
        into += ProxyNode(tag, type, host, port, raw)
        return true
    }

    private fun xrayWireguard(obj: JSONObject, settings: JSONObject, into: MutableList<ProxyNode>, containerName: String): Boolean {
        val peer = settings.optJSONArray("peers")?.optJSONObject(0) ?: return false
        val endpoint = peer.string("endpoint") ?: return false
        val host = endpoint.substringBeforeLast(':').removeSurrounding("[", "]")
        val port = endpoint.substringAfterLast(':').toIntOrNull()?.takeIf { it in 1..65535 } ?: return false
        val addresses = settings.optJSONArray("address")?.let { array -> (0 until array.length()).map(array::getString) }
            ?: settings.string("address")?.let { listOf(it) } ?: return false
        val source = JSONObject().put("private_key", settings.string("secretKey") ?: return false).put("address", JSONArray(addresses))
            .put("peers", JSONArray().put(JSONObject().put("address", host).put("port", port).put("public_key", peer.string("publicKey") ?: return false).apply {
                peer.string("preSharedKey")?.let { put("pre_shared_key", it) }
                peer.optJSONArray("allowedIPs")?.let { put("allowed_ips", it) }
            }))
        settings.integer("mtu")?.let { source.put("mtu", it) }
        settings.optJSONArray("reserved")?.let { source.put("reserved", it) }
        val endpointJson = ExtraProtocols.wireguardEndpoint(source) ?: return false
        into += ExtraProtocols.nodeOf(endpointJson, containerName.ifBlank { obj.string("tag") })
        return true
    }

    private fun parseClash(text: String): List<ProxyNode> {
        val lines = text.lineSequence().toList()
        val start = lines.indexOfFirst { it.trim() == "proxies:" }
        if (start < 0) return emptyList()
        val maps = mutableListOf<MutableMap<String, String>>()
        var current: MutableMap<String, String>? = null
        val parents = mutableListOf<Pair<Int, String>>()
        var itemIndent = -1
        val listCounts = mutableMapOf<String, Int>()
        for (line in lines.drop(start + 1)) {
            val trimmed = line.trim()
            if (trimmed.isEmpty() || trimmed.startsWith("#")) continue
            val indent = line.indexOfFirst { !it.isWhitespace() }.coerceAtLeast(0)
            if (indent == 0 && trimmed != "proxies:") break
            if (trimmed.startsWith("- ") && current != null && itemIndent >= 0 && indent > itemIndent) {
                while (parents.isNotEmpty() && parents.last().first > indent) parents.removeAt(parents.lastIndex)
                if (parents.isNotEmpty()) {
                    val listKey = parents.joinToString(".") { it.second }
                    val position = listCounts.merge(listKey, 1, Int::plus)!! - 1
                    current["$listKey.$position"] = unquote(trimmed.drop(2).trim())
                }
            } else if (trimmed.startsWith("- ")) {
                if (itemIndent < 0) itemIndent = indent
                listCounts.clear()
                current = linkedMapOf()
                maps += current
                parents.clear()
                val rest = trimmed.drop(2).trim()
                if (rest.startsWith("{") && rest.endsWith("}")) current.putAll(parseInlineMap(rest.substring(1, rest.length - 1)))
                else parsePair(rest)?.let { (k, v) -> current[k] = v }
            } else if (current != null) {
                val pair = parsePair(trimmed) ?: continue
                while (parents.isNotEmpty() && parents.last().first >= indent) parents.removeAt(parents.lastIndex)
                if (pair.second.isEmpty()) {
                    parents += indent to pair.first
                } else {
                    val key = (parents.map { it.second } + pair.first).joinToString(".")
                    if (pair.second.startsWith("{") && pair.second.endsWith("}")) {
                        parseInlineMap(pair.second.substring(1, pair.second.length - 1)).forEach { (child, value) -> current["$key.$child"] = value }
                    } else current[key] = pair.second
                }
            }
        }
        return maps.mapNotNull(::clashNode)
    }

    private fun clashNode(map: Map<String, String>): ProxyNode? {
        val sourceType = map["type"]?.lowercase() ?: return null
        val type = when (sourceType) { "ss" -> "shadowsocks"; "hy2" -> "hysteria2"; else -> sourceType }
        if (type == "wireguard") return clashWireguard(map)
        if (type !in knownTypes) return null
        if (!map["plugin"].isNullOrBlank()) return null
        val host = map["server"] ?: return null
        val port = map["port"]?.toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
        val tag = map["name"] ?: "$type • $host"
        val out = JSONObject().put("type", type).put("tag", tag).put("server", host).put("server_port", port)
        when (type) {
            "vless", "vmess" -> map["uuid"]?.let { out.put("uuid", it) }
            "tuic" -> { map["uuid"]?.let { out.put("uuid", it) }; map["password"]?.let { out.put("password", it) } }
            "trojan", "hysteria2", "shadowsocks", "anytls" -> map["password"]?.let { out.put("password", it) }
            "hysteria" -> {
                (map["auth-str"] ?: map["auth_str"] ?: map["auth"])?.let { out.put("auth_str", it) }
                out.put("up_mbps", mbps(map["up"]) ?: 50).put("down_mbps", mbps(map["down"]) ?: 100)
                map["obfs"]?.takeIf(String::isNotBlank)?.let { out.put("obfs", it) }
            }
            "ssh" -> {
                map["username"]?.let { out.put("user", it) }
                map["password"]?.let { out.put("password", it) }
                map["private-key"]?.let { out.put("private_key", it) }
                map["private-key-passphrase"]?.let { out.put("private_key_passphrase", it) }
            }
            "naive" -> { map["username"]?.let { out.put("username", it) }; map["password"]?.let { out.put("password", it) } }
            "http", "socks" -> { map["username"]?.let { out.put("username", it) }; map["password"]?.let { out.put("password", it) } }
        }
        if (type == "shadowsocks") out.put("method", map["cipher"] ?: map["method"] ?: "")
        if (type == "vmess") { out.put("alter_id", map["alterId"]?.toIntOrNull() ?: 0); out.put("security", map["cipher"] ?: "auto") }
        if (type == "vless") map["flow"]?.let { out.put("flow", it) }
        if (type in setOf("trojan", "hysteria2", "hysteria", "anytls", "tuic", "naive") || map["tls"] == "true") {
            val tls = JSONObject().put("enabled", true)
            (map["servername"] ?: map["sni"])?.let { tls.put("server_name", it) }
            if (map["skip-cert-verify"] == "true") tls.put("insecure", true)
            (map["alpn"] ?: map["alpn.0"])?.let { value ->
                val list = (map.filterKeys { it.startsWith("alpn.") }.values + value.removeSurrounding("[", "]").split(',').map { it.trim().trim('"', '\'') }).filter(String::isNotBlank).distinct()
                if (list.isNotEmpty()) tls.put("alpn", JSONArray(list))
            }
            map["client-fingerprint"]?.let { tls.put("utls", JSONObject().put("enabled", true).put("fingerprint", it)) }
            if (type == "vless" && (map["reality-opts.public-key"] != null || map["reality-opts"] != null)) {
                val realityOpts = parseInlineMap(map["reality-opts"].orEmpty().removeSurrounding("{", "}")) + map.filterKeys { it.startsWith("reality-opts.") }.mapKeys { it.key.removePrefix("reality-opts.") }
                val reality = JSONObject().put("enabled", true)
                realityOpts["public-key"]?.let { reality.put("public_key", it) }
                realityOpts["short-id"]?.let { reality.put("short_id", it) }
                tls.put("reality", reality)
            }
            out.put("tls", tls)
        }
        when (map["network"]?.lowercase()) {
            "ws", "websocket" -> {
                val ws = parseInlineMap(map["ws-opts"].orEmpty().removeSurrounding("{", "}")) + map.filterKeys { it.startsWith("ws-opts.") }.mapKeys { it.key.removePrefix("ws-opts.") }
                val transport = JSONObject().put("type", "ws")
                (ws["path"] ?: map["ws-path"] ?: map["path"])?.let { transport.put("path", it) }
                (ws.entries.firstOrNull { it.key.equals("headers.host", true) }?.value ?: map["host"])?.let { transport.put("headers", JSONObject().put("Host", it)) }
                out.put("transport", transport)
            }
            "grpc" -> out.put("transport", JSONObject().put("type", "grpc").put("service_name", map["grpc-service-name"] ?: ""))
            "http", "h2" -> {
                val http = parseInlineMap(map["http-opts"].orEmpty().removeSurrounding("{", "}"))
                val transport = JSONObject().put("type", "http")
                (http["path"] ?: map["path"])?.let { transport.put("path", it) }
                (http["host"] ?: map["host"])?.split(',')?.map(String::trim)?.filter(String::isNotBlank)
                    ?.takeIf { it.isNotEmpty() }?.let { transport.put("host", JSONArray(it)) }
                out.put("transport", transport)
            }
        }
        if (type == "hysteria2") map["obfs-password"]?.let { out.put("obfs", JSONObject().put("type", map["obfs"] ?: "salamander").put("password", it)) }
        return ProxyNode(tag, type, host, port, out.toString())
    }

    private fun clashWireguard(map: Map<String, String>): ProxyNode? {
        val host = map["server"] ?: return null
        val port = map["port"]?.toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
        val addresses = listOfNotNull(map["ip"], map["ipv6"]).flatMap { it.split(',') }.map(String::trim).filter(String::isNotBlank)
        val reserved = map["reserved"]?.removeSurrounding("[", "]")?.split(',')?.mapNotNull { it.trim().toIntOrNull() }?.takeIf { it.size == 3 }
        val source = JSONObject().put("private_key", map["private-key"] ?: return null).put("address", JSONArray(addresses))
            .put("peers", JSONArray().put(JSONObject().put("address", host).put("port", port).put("public_key", map["public-key"] ?: return null).apply {
                map["pre-shared-key"]?.let { put("pre_shared_key", it) }
                reserved?.let { put("reserved", JSONArray(it)) }
            }))
        map["mtu"]?.toIntOrNull()?.let { source.put("mtu", it) }
        val endpoint = ExtraProtocols.wireguardEndpoint(source) ?: return null
        return ExtraProtocols.nodeOf(endpoint, map["name"])
    }

    private fun mbps(value: String?): Int? = value?.filter(Char::isDigit)?.toIntOrNull()?.takeIf { it > 0 }

    private fun parsePair(value: String): Pair<String, String>? {
        val separator = value.indexOf(':')
        if (separator <= 0) return null
        val key = value.substring(0, separator).trim()
        val raw = value.substring(separator + 1).trim().substringBefore(" #").trim()
        return key to unquote(raw)
    }

    private fun parseInlineMap(value: String): Map<String, String> = value.split(',').mapNotNull { parsePair(it.trim()) }.toMap()
    private fun unquote(value: String): String = value.trim().removeSurrounding("\"", "\"").removeSurrounding("'", "'")
    private fun JSONObject.string(key: String): String? = opt(key).let { if (it is String) it.takeIf(String::isNotBlank) else null }
    private fun JSONObject.integer(key: String): Int? = when (val value = opt(key)) { is Number -> value.toInt(); is String -> value.toIntOrNull(); else -> null }
}
