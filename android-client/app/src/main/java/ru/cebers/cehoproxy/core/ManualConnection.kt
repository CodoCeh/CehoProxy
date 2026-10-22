package ru.cebers.cehoproxy.core

import java.nio.charset.StandardCharsets

enum class TlsMode { NONE, FIXED, SWITCH }

enum class ManualProtocol(
    val label: String, val scheme: String, val defaultPort: Int,
    val userLabel: String?, val userRequired: Boolean,
    val passwordLabel: String?, val passwordRequired: Boolean,
    val tls: TlsMode, val hint: String, val certificateCheckCanBeOff: Boolean = true,
) {
    HTTPS("HTTPS (Naive)", "naive", 8443, "Логин", true, "Пароль", true, TlsMode.FIXED,
        "Прокси по HTTPS с логином и паролем (NaiveProxy, Caddy). Порт обычно 443 или 8443.", certificateCheckCanBeOff = false),
    SOCKS5("SOCKS5", "socks5", 1080, "Логин", false, "Пароль", false, TlsMode.NONE,
        "Обычный SOCKS5-прокси. Логин и пароль нужны, только если их требует сервер."),
    HTTP("HTTP", "http", 8080, "Логин", false, "Пароль", false, TlsMode.NONE,
        "HTTP-прокси: до сервера данные не шифруются. Подходит для доверенной сети."),
    VLESS("VLESS", "vless", 443, "UUID", true, null, false, TlsMode.SWITCH,
        "Свой сервер на Xray или sing-box. Для Reality, WebSocket и других вариантов вставьте готовую ссылку."),
    VMESS("VMess", "vmess", 443, "UUID", true, null, false, TlsMode.SWITCH,
        "Свой сервер на Xray или V2Ray. Для WebSocket и других вариантов вставьте готовую ссылку."),
    TROJAN("Trojan", "trojan", 443, null, false, "Пароль", true, TlsMode.FIXED,
        "Сервер Trojan: пароль и TLS-сертификат на сервере."),
    SHADOWSOCKS("Shadowsocks", "ss", 8388, null, false, "Пароль", true, TlsMode.NONE,
        "Сервер Shadowsocks: выберите способ шифрования, как на сервере."),
    HYSTERIA2("Hysteria2", "hysteria2", 443, null, false, "Пароль", true, TlsMode.FIXED,
        "Быстрый протокол поверх UDP. Если оператор режет UDP, он не заработает."),
    TUIC("TUIC", "tuic", 443, "UUID", true, "Пароль", true, TlsMode.FIXED,
        "Протокол поверх UDP (QUIC). Нужны UUID и пароль пользователя."),
    ANYTLS("AnyTLS", "anytls", 443, null, false, "Пароль", true, TlsMode.FIXED,
        "Сервер AnyTLS: маскируется под обычный HTTPS."),
    SSH("SSH", "ssh", 22, "Логин", true, "Пароль", true, TlsMode.NONE,
        "Туннель через SSH-сервер. Сервер проверяется не по отпечатку ключа, поэтому используйте доверенную сеть."),
}

val shadowsocksMethods = listOf("aes-256-gcm", "aes-128-gcm", "chacha20-ietf-poly1305", "2022-blake3-aes-256-gcm")

/** A server typed in by hand: address, port, login and password instead of a link from a provider. */
data class ManualConnection(
    val protocol: ManualProtocol = ManualProtocol.HTTPS,
    val host: String = "",
    val port: String = "",
    val username: String = "",
    val password: String = "",
    val serverName: String = "",
    val name: String = "",
    val tls: Boolean = true,
    val insecure: Boolean = false,
    val method: String = shadowsocksMethods.first(),
)

object ManualConnectionPolicy {
    private val uuid = Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")

    /** Returns the first problem to show to the user, or null when the form can be turned into a link. */
    fun validate(input: ManualConnection): String? {
        val protocol = input.protocol
        val host = input.host.trim()
        if (host.isEmpty()) return "Укажите адрес сервера"
        if (host.contains("://") || host.any { it.isWhitespace() || it in "/@#?" }) return "Введите только адрес сервера, без http:// и пути"
        val port = input.port.trim().ifEmpty { protocol.defaultPort.toString() }.toIntOrNull()
        if (port == null || port !in 1..65535) return "Порт — число от 1 до 65535"
        val username = input.username.trim()
        if (protocol == ManualProtocol.HTTPS && (username.isEmpty() || input.password.isEmpty())) return "Для HTTPS-прокси нужны логин и пароль"
        if (protocol.userLabel != null && protocol.userRequired && username.isEmpty()) return "Укажите ${protocol.userLabel.lowercase().let { if (it == "uuid") "UUID" else it }}"
        if (protocol.userLabel == "UUID" && !uuid.matches(username)) return "UUID — 36 символов вида 11111111-2222-3333-4444-555555555555"
        if (protocol.passwordLabel != null && protocol.passwordRequired && input.password.isEmpty()) return "Укажите пароль"
        if (protocol.userLabel != null && !protocol.userRequired && username.isEmpty() && input.password.isNotEmpty()) return "Укажите логин к этому паролю"
        if (protocol == ManualProtocol.SHADOWSOCKS && input.method !in shadowsocksMethods) return "Выберите способ шифрования"
        return null
    }

    /** Builds the share link that the ordinary importer already understands. Call only when [validate] returned null. */
    fun toUri(input: ManualConnection): String {
        val protocol = input.protocol
        val host = input.host.trim().let { if (it.contains(':') && !it.startsWith("[")) "[$it]" else it }
        val port = input.port.trim().ifEmpty { protocol.defaultPort.toString() }
        val username = input.username.trim()
        val label = input.name.trim().ifEmpty { "${protocol.label} • ${input.host.trim()}" }
        val sni = input.serverName.trim().takeIf { it.isNotEmpty() }
        val tlsOn = when (protocol.tls) { TlsMode.NONE -> false; TlsMode.FIXED -> true; TlsMode.SWITCH -> input.tls }
        val insecure = tlsOn && protocol.certificateCheckCanBeOff && input.insecure
        fun query(vararg fixed: String) = (fixed.toList() + listOfNotNull(
            sni?.takeIf { tlsOn }?.let { "sni=" + encode(it) }, "insecure=1".takeIf { insecure },
        )).joinToString("&").let { if (it.isEmpty()) "" else "?$it" }
        return when (protocol) {
            ManualProtocol.HTTPS, ManualProtocol.SOCKS5, ManualProtocol.HTTP -> {
                val auth = if (username.isEmpty()) "" else encode(username) + (if (input.password.isNotEmpty()) ":" + encode(input.password) else "") + "@"
                val q = if (protocol == ManualProtocol.HTTPS) (sni?.let { "?sni=" + encode(it) } ?: "") else ""
                "${protocol.scheme}://$auth$host:$port$q#${encode(label)}"
            }
            ManualProtocol.VLESS -> "vless://${encode(username)}@$host:$port${query("security=" + if (tlsOn) "tls" else "none", "type=tcp")}#${encode(label)}"
            ManualProtocol.VMESS -> "vmess://" + java.util.Base64.getEncoder().encodeToString(org.json.JSONObject()
                .put("v", "2").put("ps", label).put("add", input.host.trim()).put("port", port).put("id", username).put("aid", "0").put("scy", "auto")
                .put("net", "tcp").put("type", "none").put("tls", if (tlsOn) "tls" else "").apply {
                    if (tlsOn) put("sni", sni ?: input.host.trim())
                    if (insecure) put("allowInsecure", "1")
                }.toString().toByteArray(StandardCharsets.UTF_8))
            ManualProtocol.TROJAN -> "trojan://${encode(input.password)}@$host:$port${query()}#${encode(label)}"
            ManualProtocol.SHADOWSOCKS -> "ss://" + java.util.Base64.getUrlEncoder().withoutPadding()
                .encodeToString("${input.method}:${input.password}".toByteArray(StandardCharsets.UTF_8)) + "@$host:$port#${encode(label)}"
            ManualProtocol.HYSTERIA2 -> "hysteria2://${encode(input.password)}@$host:$port${query()}#${encode(label)}"
            ManualProtocol.TUIC -> "tuic://" + userInfo(username, input.password) + "@$host:$port${query("alpn=h3")}#${encode(label)}"
            ManualProtocol.ANYTLS -> "anytls://${encode(input.password)}@$host:$port${query()}#${encode(label)}"
            ManualProtocol.SSH -> "ssh://" + userInfo(username, input.password) + "@$host:$port#${encode(label)}"
        }
    }

    private fun userInfo(user: String, password: String) = encode(user) + ":" + encode(password)

    private fun encode(value: String): String = buildString {
        value.toByteArray(StandardCharsets.UTF_8).forEach { byte ->
            val c = (byte.toInt() and 0xFF).toChar()
            if (c in 'A'..'Z' || c in 'a'..'z' || c in '0'..'9' || c in "-._~") append(c) else append('%').append("%02X".format(byte.toInt() and 0xFF))
        }
    }
}

internal object NodeRemovalPolicy {
    /** Only nodes that came without a subscription (typed in by hand or from a single link) can be removed one by one. */
    fun removable(profile: ClientProfile, node: ProxyNode): Boolean =
        SubscriptionRemovalPolicy.urlFor(profile, node.subscriptionMetadata) == null && node.subscriptionMetadata.sourceHost == null

    fun remove(profile: ClientProfile, raw: String): ClientProfile {
        val index = profile.nodes.indexOfFirst { it.raw == raw }
        if (index < 0) return profile
        val removed = profile.nodes[index]
        val selectedRaw = profile.selectedNode?.raw
        val kept = profile.nodes.filterNot { it.raw == raw }
        val selected = kept.indexOfFirst { it.raw == selectedRaw }.takeIf { it >= 0 } ?: index.coerceAtMost((kept.size - 1).coerceAtLeast(0))
        val id = removed.routeId
        return profile.copy(
            nodes = kept, selected = selected,
            appNodeRoutes = profile.appNodeRoutes.filterValues { it != id },
            siteNodeRoutes = profile.siteNodeRoutes.filterValues { it != id },
            autoExcludedRouteIds = profile.autoExcludedRouteIds - id,
        )
    }
}
