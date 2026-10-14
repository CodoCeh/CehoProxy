package ru.cebers.cehoproxy.core

object UptimeFormat {
    fun label(elapsedMillis: Long): String {
        val seconds = elapsedMillis.coerceAtLeast(0L) / 1000
        return "%02d:%02d:%02d".format(seconds / 3600, seconds % 3600 / 60, seconds % 60)
    }
}
