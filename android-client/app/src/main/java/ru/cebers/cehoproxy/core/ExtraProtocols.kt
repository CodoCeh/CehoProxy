package ru.cebers.cehoproxy.core

import java.net.URI
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import org.json.JSONArray
import org.json.JSONObject

/** Share links and files for protocols that sing-box runs as plain JSON: AnyTLS, Hysteria 1, WireGuard, SSH. */
internal object ExtraProtocols {
    val linkSchemes = setOf("anytls", "hysteria", "hy", "wireguard", "wg", "ssh")
    private val defaultAllowedIps = listOf("0.0.0.0/0", "::/0")

    fun parseLink(line: String): ProxyNode? = runCatching {
        val uri = URI(line)
        val scheme = uri.scheme?.lowercase() ?: return null
        if (scheme !in linkSchemes) return null
        val host = uri.host?.removeSurrounding("[", "]")?.takeIf(String::isNotBlank) ?: return null
        val port = uri.port.takeIf { it in 1..65535 } ?: defaultPort(scheme) ?: return null
        val query = queryOf(uri)
        val label = uri.rawFragment?.let(::decode)?.takeIf(String::isNotBlank)
        val user = uri.rawUserInfo.orEmpty()
        when (scheme) {
            "anytls" -> {
                val out = base("anytls", host, port).put("password", decode(user))
                tls(host, query, alwaysOn = true)?.let { out.put("tls", it) }
                node(out, label)
            }
            "hysteria", "hy" -> {
                val protocol = query["protocol"].orEmpty().ifBlank { "udp" }.lowercase()
                require(protocol == "udp") { "Hysteria 1 поверх $protocol не поддерживается" }
                val out = base("hysteria", host, port)
                (query["auth"] ?: query["auth_str"] ?: user.takeIf(String::isNotBlank)?.let(::decode))?.let { out.put("auth_str", it) }
                out.put("up_mbps", mbps(query["upmbps"] ?: query["up"]) ?: 50).put("down_mbps", mbps(query["downmbps"] ?: query["down"]) ?: 100)
                query["obfsParam"]?.takeIf(String::isNotBlank)?.let { out.put("obfs", it) }
                    ?: query["obfs"]?.takeIf { it.isNotBlank() && it != "xplus" }?.let { out.put("obfs", it) }
                tls(host, query, alwaysOn = true)?.let { out.put("tls", it) }
                node(out, label)
            }
            "wireguard", "wg" -> wireguardLink(host, port, user, query, label)
            "ssh" -> {
                val out = base("ssh", host, port)
                val login = user.substringBefore(':').let(::decode)
                require(login.isNotBlank()) { "Для SSH нужен логин" }
                out.put("user", login)
                if (':' in user) out.put("password", decode(user.substringAfter(':')))
                query["private_key"]?.takeIf(String::isNotBlank)?.let { out.put("private_key", it) }
                query["private_key_passphrase"]?.takeIf(String::isNotBlank)?.let { out.put("private_key_passphrase", it) }
                require(out.has("password") || out.has("private_key")) { "Для SSH нужен пароль или ключ" }
                node(out, label)
            }
            else -> null
        }
    }.getOrNull()

    /** A standard WireGuard `.conf` file: one `[Interface]` and one or more `[Peer]` blocks. */
    fun parseWireguardConfig(text: String): List<ProxyNode> = runCatching {
        if (!text.contains("[Interface]", ignoreCase = true) || !text.contains("[Peer]", ignoreCase = true)) return emptyList()
        val sections = mutableListOf<Pair<String, MutableMap<String, String>>>()
        text.lineSequence().map { it.substringBefore('#').trim() }.filter(String::isNotEmpty).forEach { line ->
            if (line.startsWith("[") && line.endsWith("]")) sections += line.substring(1, line.length - 1).lowercase() to linkedMapOf()
            else sections.lastOrNull()?.second?.let { map ->
                val eq = line.indexOf('=')
                if (eq > 0) map[line.substring(0, eq).trim().lowercase()] = line.substring(eq + 1).trim()
            }
        }
        val iface = sections.firstOrNull { it.first == "interface" }?.second ?: return emptyList()
        val privateKey = iface["privatekey"] ?: return emptyList()
        val addresses = list(iface["address"]).ifEmpty { return emptyList() }
        sections.filter { it.first == "peer" }.mapIndexedNotNull { index, (_, peer) ->
            val endpoint = peer["endpoint"] ?: return@mapIndexedNotNull null
            val host = endpoint.substringBeforeLast(':').removeSurrounding("[", "]")
            val port = endpoint.substringAfterLast(':').toIntOrNull()?.takeIf { it in 1..65535 } ?: return@mapIndexedNotNull null
            val out = endpoint(host, port, privateKey, addresses, peer["publickey"] ?: return@mapIndexedNotNull null, peer["presharedkey"],
                list(peer["allowedips"]).ifEmpty { defaultAllowedIps }, iface["mtu"]?.toIntOrNull(), null)
            node(out, if (index == 0) null else "wireguard • $host (${index + 1})")
        }
    }.getOrDefault(emptyList())

    /** A WireGuard peer written the way sing-box 1.11+ and Xray write it, or the old sing-box outbound form. */
    fun wireguardEndpoint(source: JSONObject): JSONObject? {
        val peers = source.optJSONArray("peers")
        val peer = peers?.optJSONObject(0)
        val host = peer?.optString("address").orEmpty().ifBlank { peer?.optString("server").orEmpty() }.ifBlank { source.optString("server") }
        val port = (peer?.optInt("port", 0) ?: 0).takeIf { it in 1..65535 } ?: source.optInt("server_port", 0).takeIf { it in 1..65535 } ?: return null
        val publicKey = peer?.optString("public_key").orEmpty().ifBlank { source.optString("peer_public_key") }
        val privateKey = source.optString("private_key")
        val addresses = source.optJSONArray("address")?.let { array -> (0 until array.length()).map(array::getString) }
            ?: source.optJSONArray("local_address")?.let { array -> (0 until array.length()).map(array::getString) } ?: emptyList()
        if (host.isBlank() || publicKey.isBlank() || privateKey.isBlank() || addresses.isEmpty()) return null
        val out = endpoint(host, port, privateKey, addresses, publicKey, peer?.optString("pre_shared_key")?.takeIf(String::isNotBlank) ?: source.optString("pre_shared_key").takeIf(String::isNotBlank),
            peer?.optJSONArray("allowed_ips")?.let { array -> (0 until array.length()).map(array::getString) } ?: defaultAllowedIps,
            source.optInt("mtu", 0).takeIf { it > 0 }, (peer?.optJSONArray("reserved") ?: source.optJSONArray("reserved"))?.let { array -> (0 until array.length()).map(array::getInt) })
        source.optString("tag").takeIf(String::isNotBlank)?.let { out.put("tag", it) }
        return out
    }

    fun nodeOf(endpoint: JSONObject, name: String? = null): ProxyNode = node(endpoint, name)

    private fun wireguardLink(host: String, port: Int, privateKey: String, query: Map<String, String>, label: String?): ProxyNode {
        val publicKey = query["publickey"] ?: query["public_key"] ?: query["peer_public_key"] ?: throw IllegalArgumentException("Нет публичного ключа сервера")
        val addresses = list(query["address"] ?: query["ip"]).ifEmpty { throw IllegalArgumentException("Нет адреса клиента") }
        val reserved = query["reserved"]?.split(',')?.mapNotNull { it.trim().toIntOrNull() }?.takeIf { it.size == 3 }
        val out = endpoint(host, port, decode(privateKey), addresses, publicKey, query["presharedkey"] ?: query["pre_shared_key"],
            list(query["allowedips"]).ifEmpty { defaultAllowedIps }, (query["mtu"])?.toIntOrNull(), reserved)
        return node(out, label)
    }

    private fun endpoint(host: String, port: Int, privateKey: String, addresses: List<String>, publicKey: String, presharedKey: String?,
        allowedIps: List<String>, mtu: Int?, reserved: List<Int>?): JSONObject {
        val peer = JSONObject().put("address", host).put("port", port).put("public_key", publicKey).put("allowed_ips", JSONArray(allowedIps))
        presharedKey?.takeIf(String::isNotBlank)?.let { peer.put("pre_shared_key", it) }
        reserved?.let { peer.put("reserved", JSONArray(it)) }
        return JSONObject().put("type", "wireguard").put("tag", "wireguard • $host").put("system", false)
            .put("address", JSONArray(addresses.map { if ('/' in it) it else if (':' in it) "$it/128" else "$it/32" }))
            .put("private_key", privateKey).put("peers", JSONArray().put(peer)).apply { mtu?.let { put("mtu", it) } }
    }

    private fun node(out: JSONObject, label: String?): ProxyNode {
        val type = out.getString("type")
        val (host, port) = if (type == "wireguard") out.getJSONArray("peers").getJSONObject(0).let { it.getString("address") to it.getInt("port") }
        else out.getString("server") to out.getInt("server_port")
        val name = label ?: out.optString("tag").takeIf(String::isNotBlank) ?: "$type • $host"
        out.put("tag", name)
        return ProxyNode(name, type, host, port, out.toString())
    }

    private fun base(type: String, host: String, port: Int) = JSONObject().put("type", type).put("server", host).put("server_port", port)

    fun tls(host: String, query: Map<String, String>, alwaysOn: Boolean): JSONObject? {
        if (!alwaysOn && query["security"] != "tls") return null
        val tls = JSONObject().put("enabled", true)
        tls.put("server_name", (query["sni"] ?: query["peer"] ?: query["servername"])?.takeIf(String::isNotBlank) ?: host)
        if (truthy(query["insecure"]) || truthy(query["allowInsecure"]) || truthy(query["skip-cert-verify"])) tls.put("insecure", true)
        query["alpn"]?.split(',')?.map(String::trim)?.filter(String::isNotBlank)?.takeIf { it.isNotEmpty() }?.let { tls.put("alpn", JSONArray(it)) }
        query["fp"]?.takeIf(String::isNotBlank)?.let { tls.put("utls", JSONObject().put("enabled", true).put("fingerprint", it)) }
        return tls
    }

    private fun defaultPort(scheme: String): Int? = when (scheme) { "ssh" -> 22; "anytls" -> 443; else -> null }
    private fun mbps(value: String?): Int? = value?.filter(Char::isDigit)?.toIntOrNull()?.takeIf { it > 0 }
    private fun truthy(value: String?) = value == "1" || value.equals("true", ignoreCase = true)
    private fun list(value: String?): List<String> = value.orEmpty().split(',').map(String::trim).filter(String::isNotBlank)
    private fun decode(value: String) = URLDecoder.decode(value.replace("+", "%2B"), StandardCharsets.UTF_8.name())
    private fun queryOf(uri: URI): Map<String, String> = uri.rawQuery.orEmpty().split('&').mapNotNull { entry ->
        val split = entry.indexOf('=')
        if (split < 0) null else decode(entry.substring(0, split)) to decode(entry.substring(split + 1))
    }.toMap()
}
