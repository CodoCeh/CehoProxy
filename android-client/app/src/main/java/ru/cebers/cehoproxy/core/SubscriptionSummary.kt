package ru.cebers.cehoproxy.core

import java.util.Locale

/** Facts shown in the home summary: remaining term and traffic share, taken only from what the provider sent. */
internal object SubscriptionSummary {
    private const val DAY_SECONDS = 86_400L

    enum class TermKind { UNKNOWN, ACTIVE, LAST_DAY, EXPIRED }

    data class Term(val kind: TermKind, val label: String, val days: Long?, val expiresAt: Long?)

    fun term(expiresAtEpochSeconds: Long?, nowEpochSeconds: Long): Term {
        val at = expiresAtEpochSeconds?.takeIf { it > 0 } ?: return Term(TermKind.UNKNOWN, "Не указан", null, null)
        val left = at - nowEpochSeconds
        return when {
            left <= 0 -> Term(TermKind.EXPIRED, "Истекла", 0, at)
            left < DAY_SECONDS -> Term(TermKind.LAST_DAY, "Менее суток", 0, at)
            else -> (left / DAY_SECONDS).let { days -> Term(TermKind.ACTIVE, "$days ${plural(days, "день", "дня", "дней")}", days, at) }
        }
    }

    enum class TrafficKind { NONE, LIMITED, LIMIT_ONLY, OVER_LIMIT, UNKNOWN_LIMIT, UNLIMITED }

    /** [fraction] is the used share of the limit; it is null whenever no honest share exists. */
    data class Traffic(val kind: TrafficKind, val used: Long?, val total: Long?, val remaining: Long?, val fraction: Float?)

    /** A missing limit is unknown; a limit of zero is how providers declare "no limit". */
    fun traffic(usedBytes: Long?, totalBytes: Long?): Traffic {
        val used = usedBytes?.coerceAtLeast(0)
        val total = totalBytes?.coerceAtLeast(0)
        return when {
            used == null && total == null -> Traffic(TrafficKind.NONE, null, null, null, null)
            total == 0L -> Traffic(TrafficKind.UNLIMITED, used, 0, null, null)
            total == null -> Traffic(TrafficKind.UNKNOWN_LIMIT, used, null, null, null)
            used == null -> Traffic(TrafficKind.LIMIT_ONLY, null, total, null, null)
            used > total -> Traffic(TrafficKind.OVER_LIMIT, used, total, 0, 1f)
            else -> Traffic(TrafficKind.LIMITED, used, total, total - used, (used.toDouble() / total).toFloat().coerceIn(0f, 1f))
        }
    }

    fun bytes(value: Long): String {
        if (value < 1024) return "$value Б"
        val units = listOf("КиБ", "МиБ", "ГиБ", "ТиБ")
        var amount = value.toDouble()
        var unit = -1
        while (amount >= 1024 && unit < units.lastIndex) { amount /= 1024; unit++ }
        return String.format(Locale.getDefault(), "%.1f %s", amount, units[unit])
    }

    fun plural(n: Long, one: String, few: String, many: String): String {
        val tail = n % 100
        return when {
            tail in 11..14 -> many
            n % 10 == 1L -> one
            n % 10 in 2..4 -> few
            else -> many
        }
    }
}
