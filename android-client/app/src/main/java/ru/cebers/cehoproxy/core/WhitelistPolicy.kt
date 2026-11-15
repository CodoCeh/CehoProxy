package ru.cebers.cehoproxy.core

internal object WhitelistPolicy {
    private val marker = Regex("(?<![\\p{L}\\p{N}])бс(?![\\p{L}\\p{N}])|🛡", RegexOption.IGNORE_CASE)

    fun isMarked(name: String): Boolean = marker.containsMatchIn(name)

    fun preferMarked(tags: List<String>, marked: Set<String>): List<String> = tags.filter { it in marked } + tags.filterNot { it in marked }

    fun outageText(hasMarkedNodes: Boolean, autoRotation: Boolean): String = when {
        !hasMarkedNodes -> "Похоже, оператор включил белые списки: открыты только российские сайты. В подписке нет серверов для белых списков (с меткой «БС» или 🛡)"
        !autoRotation -> "Похоже, оператор включил белые списки. Включите автовыбор узла, чтобы CehoProxy сам переключался на серверы для белых списков"
        else -> "Похоже, оператор включил белые списки. Серверы для белых списков сейчас не отвечают"
    }
}
