package ru.cebers.cehoproxy.core

data class ConnectionSample(
    val id: String,
    val packageName: String,
    val outbound: String,
    val chain: List<String>,
    val rule: String,
    val host: String,
    val uplink: Long,
    val downlink: Long,
    val closed: Boolean,
)

enum class RouteKind { VPN, DIRECT, BLOCKED }

data class AppTrafficRow(
    val packageName: String,
    val kind: RouteKind,
    val exits: List<String>,
    val rules: List<String>,
    val active: Int,
    val noReply: Int,
    val hosts: List<String>,
    val uplink: Long,
    val downlink: Long,
)

object TrafficRows {
    const val UNKNOWN_APP = ""

    /** The engine lists the chain from the node that carried the traffic back to the rule's outbound. */
    fun exitTag(sample: ConnectionSample): String = sample.chain.firstOrNull()?.takeIf(String::isNotBlank) ?: sample.outbound

    fun kind(sample: ConnectionSample): RouteKind = when (exitTag(sample)) {
        "direct" -> RouteKind.DIRECT
        "block" -> RouteKind.BLOCKED
        else -> RouteKind.VPN
    }

    fun ruleLabel(rule: String): String {
        val text = rule.lowercase()
        return when {
            text.isBlank() || text.startsWith("final") -> "Основной маршрут"
            "package_name" in text -> "Правило для приложения"
            "rule_set" in text -> "Набор сайтов страны"
            "ip_version" in text -> "IPv6 закрыт"
            "protocol=dns" in text || "hijack-dns" in text -> "Запрос DNS"
            "domain" in text -> "Список сайтов"
            "ip_cidr" in text || "ip_is_private" in text -> "Адреса домашней сети"
            "reject" in text -> "Блокировка"
            else -> "Другое правило"
        }
    }

    /** Apps with any direct or blocked connection come first: those are the ones that "do not work". */
    fun rows(samples: List<ConnectionSample>, exitLabel: (String) -> String): List<AppTrafficRow> =
        samples.groupBy { it.packageName }.map { (packageName, group) ->
            val kinds = group.map(::kind).toSet()
            val kind = when {
                RouteKind.BLOCKED in kinds -> RouteKind.BLOCKED
                RouteKind.DIRECT in kinds -> RouteKind.DIRECT
                else -> RouteKind.VPN
            }
            AppTrafficRow(
                packageName = packageName,
                kind = kind,
                exits = group.map { if (kind(it) == RouteKind.VPN) exitLabel(exitTag(it)) else exitTag(it) }.distinct(),
                rules = group.map { ruleLabel(it.rule) }.distinct(),
                active = group.count { !it.closed },
                noReply = group.count { it.closed && it.downlink == 0L },
                hosts = group.map { it.host }.filter(String::isNotBlank).distinct().take(5),
                uplink = group.sumOf { it.uplink },
                downlink = group.sumOf { it.downlink },
            )
        }.sortedWith(compareBy<AppTrafficRow> { it.kind == RouteKind.VPN }.thenByDescending { it.uplink + it.downlink })

    /** The profile change that sends [packageName] through the tunnel, or null when it already goes there. */
    fun throughVpn(profile: ClientProfile, packageName: String): ClientProfile? {
        val withoutDirect = profile.localDpiApps - packageName
        val apps = if (profile.excludeSelectedApps) profile.apps - packageName else if (profile.apps.isEmpty() || packageName in profile.apps) profile.apps else profile.apps + packageName
        val next = profile.copy(localDpiApps = withoutDirect, apps = apps)
        return next.takeIf { it != profile }
    }

    fun bytes(value: Long): String = when {
        value >= 1L shl 30 -> String.format(java.util.Locale.US, "%.1f ГБ", value / (1L shl 30).toDouble()).replace('.', ',')
        value >= 1L shl 20 -> String.format(java.util.Locale.US, "%.1f МБ", value / (1L shl 20).toDouble()).replace('.', ',')
        value >= 1L shl 10 -> "${value shr 10} КБ"
        else -> "$value Б"
    }
}
