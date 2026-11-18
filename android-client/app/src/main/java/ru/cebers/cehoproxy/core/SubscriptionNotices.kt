package ru.cebers.cehoproxy.core

data class SubscriptionNotice(val title: String, val text: String)

internal object SubscriptionNotices {
    fun isStub(node: ProxyNode): Boolean = node.port <= 1 && node.server in setOf("0.0.0.0", "::", "127.0.0.1")

    fun of(profile: ClientProfile): List<SubscriptionNotice> =
        profile.nodes.filter { it.subscriptionMetadata.sourceUrl != null || it.subscriptionMetadata.sourceHost != null }
            .groupBy(NodePresentation::subscriptionKey)
            .values.mapNotNull { nodes ->
                val meta = nodes.first().subscriptionMetadata
                val source = NodePresentation.subscriptionSource(nodes.first())
                val title = if (source.startsWith("Подписка: ")) "Подписка «${source.removePrefix("Подписка: ")}» не работает" else "Подписка не работает"
                val used = meta.usedBytes
                val total = meta.totalBytes
                when {
                    nodes.all(::isStub) -> {
                        val said = nodes.map { it.name.trim() }.filter(String::isNotBlank).distinct().joinToString(" · ")
                        SubscriptionNotice(title, (if (said.isNotBlank()) "Провайдер вместо серверов прислал сообщение: «$said». " else "Провайдер не прислал ни одного сервера. ") +
                            "Так бывает, когда закончился трафик или срок подписки: продлите её у провайдера и обновите подписку.")
                    }
                    used != null && total != null && total > 0 && used >= total ->
                        SubscriptionNotice(title, "Трафик подписки закончился: использовано ${TrafficRows.bytes(used)} из ${TrafficRows.bytes(total)}. Продлите подписку у провайдера.")
                    else -> null
                }
            }
}
