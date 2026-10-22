package ru.cebers.cehoproxy.core

import java.nio.charset.StandardCharsets

enum class ManualProtocol(val label: String, val scheme: String, val defaultPort: Int, val loginRequired: Boolean) {
    HTTPS("HTTPS (Naive)", "naive", 8443, true),
    SOCKS5("SOCKS5", "socks5", 1080, false),
    HTTP("HTTP", "http", 8080, false),
}

/** A server typed in by hand: address, port, login and password instead of a link from a provider. */
data class ManualConnection(
    val protocol: ManualProtocol = ManualProtocol.HTTPS,
    val host: String = "",
    val port: String = "",
    val username: String = "",
    val password: String = "",
    val serverName: String = "",
    val name: String = "",
)

object ManualConnectionPolicy {
    /** Returns the first problem to show to the user, or null when the form can be turned into a link. */
    fun validate(input: ManualConnection): String? {
        val host = input.host.trim()
        if (host.isEmpty()) return "Укажите адрес сервера"
        if (host.contains("://") || host.any { it.isWhitespace() || it in "/@#?" }) return "Введите только адрес сервера, без http:// и пути"
        val port = input.port.trim().ifEmpty { input.protocol.defaultPort.toString() }.toIntOrNull()
        if (port == null || port !in 1..65535) return "Порт — число от 1 до 65535"
        val username = input.username.trim()
        if (input.protocol.loginRequired && username.isEmpty()) return "Для HTTPS-прокси нужны логин и пароль"
        if (input.protocol.loginRequired && input.password.isEmpty()) return "Для HTTPS-прокси нужны логин и пароль"
        if (username.isEmpty() && input.password.isNotEmpty()) return "Укажите логин к этому паролю"
        return null
    }

    /** Builds the share link that the ordinary importer already understands. Call only when [validate] returned null. */
    fun toUri(input: ManualConnection): String {
        val host = input.host.trim().let { if (it.contains(':') && !it.startsWith("[")) "[$it]" else it }
        val port = input.port.trim().ifEmpty { input.protocol.defaultPort.toString() }
        val username = input.username.trim()
        val auth = if (username.isEmpty()) "" else encode(username) + (if (input.password.isNotEmpty()) ":" + encode(input.password) else "") + "@"
        val query = buildList {
            if (input.protocol == ManualProtocol.HTTPS) {
                input.serverName.trim().takeIf { it.isNotEmpty() }?.let { add("sni=" + encode(it)) }
            }
        }.joinToString("&").let { if (it.isEmpty()) "" else "?$it" }
        val label = input.name.trim().ifEmpty { "${input.protocol.label} • ${input.host.trim()}" }
        return "${input.protocol.scheme}://$auth$host:$port$query#${encode(label)}"
    }

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
