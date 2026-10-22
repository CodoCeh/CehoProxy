package ru.cebers.cehoproxy.core

import java.util.Locale

internal object NodePresentation {
    fun subscriptionSource(node: ProxyNode): String = node.subscriptionMetadata.title?.trim()
        ?.takeIf(String::isNotBlank)?.let { title ->
            val decoded = if (title.startsWith("base64:", ignoreCase = true)) runCatching {
                String(java.util.Base64.getDecoder().decode(title.substringAfter(':')), Charsets.UTF_8).trim().takeIf(String::isNotBlank)
            }.getOrNull() ?: title else title
            "Подписка: $decoded"
        } ?: "Источник подписки не указан"

    fun subscriptionKey(node: ProxyNode): String = node.subscriptionMetadata.sourceHost ?: subscriptionSource(node)

    // Provider tags such as "[TOP 03 • 95 Mbps]" are noise in the list, the full name stays in the profile.
    fun displayName(name: String): String = name.replace(Regex("\\[[^\\]]*\\]"), "").replace(Regex("\\s+"), " ").trim()
        .ifBlank { name }

    // Node name as the subscription gives it; the server address is only a fallback for nameless nodes.
    fun routeName(node: ProxyNode): String = displayName(node.name).ifBlank { node.server }

    // The badge already shows the flag, so the title does not repeat it.
    fun withoutFlag(name: String): String = name.replace(Regex("[\\x{1F1E6}-\\x{1F1FF}]{2}"), "").replace(Regex("\\s+"), " ").trim().ifBlank { name }

    fun flag(verifiedCountryCode: String?): String? {
        val code = verifiedCountryCode?.trim()?.uppercase(Locale.ROOT) ?: return null
        if (code !in Locale.getISOCountries().toSet()) return null
        return code.map { String(Character.toChars(0x1F1E6 + it.code - 'A'.code)) }.joinToString("")
    }
}
