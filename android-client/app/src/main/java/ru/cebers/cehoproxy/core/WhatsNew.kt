package ru.cebers.cehoproxy.core

/** Окно «Что нового» после обновления. Перед выпуском добавьте запись с версией из build.gradle. */
object WhatsNew {
    data class Item(val title: String, val text: String, val where: String? = null)

    data class Release(val version: String, val items: List<Item>)

    val releases: List<Release> = listOf(
        Release("0.1.19", listOf(
            Item("Сайт не открылся — предложим исключение",
                "Если сайт не отвечает через VPN, а напрямую открывается, в уведомлении появится кнопка «Открывать напрямую». Сайт попадёт в исключения, и страница загрузится сразу.",
                "Правила подключения → Сайты → Исключения"),
        )),
    )

    /** Пункты между просмотренной и установленной версией, свежее сверху. */
    fun since(seen: String, current: String, source: List<Release> = releases): List<Item> =
        source.sortedByDescending { versionKey(it.version) }
            .filter { GitHubAndroidUpdates.newerAndroidVersion(it.version, seen) == true && GitHubAndroidUpdates.newerAndroidVersion(it.version, current) != true }
            .flatMap { it.items }

    /** Что показать сейчас и какую версию запомнить. Свежая установка окна не видит. */
    fun pending(seen: String?, current: String, freshInstall: Boolean, source: List<Release> = releases): List<Item> {
        if (seen == null && freshInstall) return emptyList()
        return since(seen ?: "0.0.0", current, source)
    }

    private fun versionKey(version: String): Long =
        version.split('.').fold(0L) { acc, part -> acc * 1000 + (part.toLongOrNull() ?: 0L) }
}
