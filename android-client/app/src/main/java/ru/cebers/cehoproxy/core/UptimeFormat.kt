package ru.cebers.cehoproxy.core

object UptimeFormat {
    fun label(elapsedMillis: Long): String {
        val seconds = elapsedMillis.coerceAtLeast(0L) / 1000
        return "%02d:%02d:%02d".format(seconds / 3600, seconds % 3600 / 60, seconds % 60)
    }

    /** Short spoken form for the home summary: "меньше минуты", "24 минуты", "1 ч 05 мин", "2 дня 3 ч". */
    fun words(elapsedMillis: Long): String {
        val minutes = elapsedMillis.coerceAtLeast(0L) / 60_000
        val hours = minutes / 60
        val days = hours / 24
        return when {
            minutes < 1 -> "меньше минуты"
            hours < 1 -> "$minutes ${SubscriptionSummary.plural(minutes, "минута", "минуты", "минут")}"
            days < 1 -> "$hours ч %02d мин".format(minutes % 60)
            else -> "$days ${SubscriptionSummary.plural(days, "день", "дня", "дней")} ${hours % 24} ч"
        }
    }
}
