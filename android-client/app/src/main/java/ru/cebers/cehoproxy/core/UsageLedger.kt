package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject

data class Usage(val uplink: Long = 0, val downlink: Long = 0) {
    val total: Long get() = uplink + downlink
    operator fun plus(other: Usage) = Usage(uplink + other.uplink, downlink + other.downlink)
}

/** Traffic counted on the phone, per app for today and this month, and per subscription for this month. Months start on the 1st. */
data class UsageLedger(
    val day: String = "",
    val month: String = "",
    val appsToday: Map<String, Usage> = emptyMap(),
    val appsMonth: Map<String, Usage> = emptyMap(),
    val subscriptionsMonth: Map<String, Usage> = emptyMap(),
) {
    fun add(packageName: String, subscription: String?, usage: Usage, today: String): UsageLedger {
        if (usage.total <= 0) return rolled(today)
        val base = rolled(today)
        return base.copy(
            appsToday = base.appsToday.plusUsage(packageName, usage),
            appsMonth = base.appsMonth.plusUsage(packageName, usage),
            subscriptionsMonth = if (subscription.isNullOrBlank()) base.subscriptionsMonth else base.subscriptionsMonth.plusUsage(subscription, usage),
        )
    }

    fun rolled(today: String): UsageLedger {
        val thisMonth = today.take(7)
        val sameMonth = month == thisMonth
        return copy(
            day = today, month = thisMonth,
            appsToday = if (day == today) appsToday else emptyMap(),
            appsMonth = if (sameMonth) appsMonth else emptyMap(),
            subscriptionsMonth = if (sameMonth) subscriptionsMonth else emptyMap(),
        )
    }

    fun encode(): String = JSONObject().put("day", day).put("month", month)
        .put("appsToday", encodeMap(appsToday)).put("appsMonth", encodeMap(appsMonth)).put("subscriptionsMonth", encodeMap(subscriptionsMonth)).toString()

    companion object {
        private fun Map<String, Usage>.plusUsage(key: String, usage: Usage) = this + (key to ((this[key] ?: Usage()) + usage))

        private fun encodeMap(map: Map<String, Usage>) = JSONObject().apply { map.forEach { (key, usage) -> put(key, JSONArray().put(usage.uplink).put(usage.downlink)) } }

        private fun decodeMap(json: JSONObject?): Map<String, Usage> = json?.keys()?.asSequence()?.associateWith { key ->
            json.optJSONArray(key)?.let { Usage(it.optLong(0).coerceAtLeast(0), it.optLong(1).coerceAtLeast(0)) } ?: Usage()
        }.orEmpty()

        fun decode(text: String?): UsageLedger = runCatching {
            val json = JSONObject(text ?: return UsageLedger())
            UsageLedger(json.optString("day"), json.optString("month"), decodeMap(json.optJSONObject("appsToday")), decodeMap(json.optJSONObject("appsMonth")), decodeMap(json.optJSONObject("subscriptionsMonth")))
        }.getOrDefault(UsageLedger())
    }
}

object QuotaPolicy {
    val thresholds = listOf(95, 80)

    /** The highest crossed warning level in percent, or 0. Unknown or zero limits never warn. */
    fun level(usedBytes: Long?, totalBytes: Long?): Int {
        val total = totalBytes?.takeIf { it > 0 } ?: return 0
        val used = usedBytes ?: return 0
        val percent = used.toDouble() * 100 / total
        return thresholds.firstOrNull { percent >= it } ?: 0
    }

    fun message(title: String, usedBytes: Long, totalBytes: Long): String =
        "Подписка «$title»: израсходовано ${Math.round(usedBytes.toDouble() * 100 / totalBytes).coerceAtMost(100)}% трафика (${TrafficRows.bytes(usedBytes)} из ${TrafficRows.bytes(totalBytes)})"
}
