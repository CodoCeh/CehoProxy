package ru.cebers.cehoproxy.core

/** Notices a site that keeps failing through the tunnel while other sites work, so the user can be offered to open it directly. */
internal class SiteFailurePolicy(
    private val windowMillis: Long = WINDOW_MILLIS,
    private val failuresNeeded: Int = FAILURES_NEEDED,
) {
    private class Entry { val failures = ArrayDeque<Long>(); var lastSuccess = 0L }

    private val entries = HashMap<String, Entry>()
    private var lastAnySuccess = Long.MIN_VALUE

    /** Records one finished connection. Returns the site to offer when the failures reach the threshold. */
    @Synchronized
    fun record(host: String, success: Boolean, nowMillis: Long): String? {
        val site = registrable(host) ?: return null
        if (success) {
            lastAnySuccess = nowMillis
            entries[site]?.let { it.lastSuccess = nowMillis; it.failures.clear() }
            return null
        }
        val entry = entries.getOrPut(site) { Entry() }
        entry.failures.addLast(nowMillis)
        while (entry.failures.isNotEmpty() && nowMillis - entry.failures.first() > windowMillis) entry.failures.removeFirst()
        if (entries.size > MAX_SITES) entries.entries.removeAll { it.value.failures.isEmpty() || nowMillis - it.value.failures.last() > windowMillis }
        val tunnelWorks = lastAnySuccess != Long.MIN_VALUE && nowMillis - lastAnySuccess <= windowMillis
        if (entry.failures.size >= failuresNeeded && tunnelWorks) {
            entries.remove(site)
            return site
        }
        return null
    }

    @Synchronized fun reset() { entries.clear(); lastAnySuccess = Long.MIN_VALUE }

    companion object {
        const val WINDOW_MILLIS = 90_000L
        const val FAILURES_NEEDED = 3
        private const val MAX_SITES = 200
        private val secondLevel = setOf("co", "com", "org", "net", "gov", "edu", "ac")
        private val ipv4 = Regex("^\\d{1,3}(\\.\\d{1,3}){3}$")

        /** Site name for the exceptions list: example.com for a.b.example.com, null for addresses and local names. */
        fun registrable(host: String): String? {
            val name = host.trim().lowercase().trimEnd('.')
            if (name.isEmpty() || ':' in name || ipv4.matches(name)) return null
            val labels = name.split('.')
            if (labels.size < 2 || labels.any { it.isEmpty() }) return null
            if (labels.last() in setOf("local", "localhost", "arpa", "internal", "lan")) return null
            return if (labels.size >= 3 && labels.last().length == 2 && labels[labels.size - 2] in secondLevel) labels.takeLast(3).joinToString(".")
            else labels.takeLast(2).joinToString(".")
        }
    }
}
