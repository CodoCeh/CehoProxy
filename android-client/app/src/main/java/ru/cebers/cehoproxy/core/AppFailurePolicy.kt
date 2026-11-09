package ru.cebers.cehoproxy.core

/** Notices an application whose connections keep failing through the tunnel while other applications work. */
internal class AppFailurePolicy(
    private val windowMillis: Long = SiteFailurePolicy.WINDOW_MILLIS,
    private val failuresNeeded: Int = FAILURES_NEEDED,
    private val sitesNeeded: Int = SITES_NEEDED,
) {
    private class Entry { val failures = ArrayDeque<Pair<Long, String>>(); var lastSuccess = Long.MIN_VALUE }

    private val entries = HashMap<String, Entry>()
    private var lastOtherSuccess = HashMap<String, Long>()

    data class Failure(val packageName: String, val sites: Set<String>)

    /** Records one finished connection of an application. Returns the application to offer when the failures reach the threshold. */
    @Synchronized
    fun record(packageName: String, host: String, success: Boolean, nowMillis: Long): Failure? {
        if (packageName.isEmpty()) return null
        if (success) {
            lastOtherSuccess[packageName] = nowMillis
            entries[packageName]?.let { it.lastSuccess = nowMillis; it.failures.clear() }
            return null
        }
        val site = SiteFailurePolicy.registrable(host) ?: return null
        val entry = entries.getOrPut(packageName) { Entry() }
        entry.failures.addLast(nowMillis to site)
        while (entry.failures.isNotEmpty() && nowMillis - entry.failures.first().first > windowMillis) entry.failures.removeFirst()
        val tunnelWorks = lastOtherSuccess.any { (other, at) -> other != packageName && nowMillis - at <= windowMillis }
        val sites = entry.failures.map { it.second }.toSet()
        if (entry.failures.size >= failuresNeeded && sites.size >= sitesNeeded && tunnelWorks) {
            entries.remove(packageName)
            return Failure(packageName, sites)
        }
        return null
    }

    @Synchronized fun reset() { entries.clear(); lastOtherSuccess.clear() }

    companion object {
        const val FAILURES_NEEDED = 5
        const val SITES_NEEDED = 2
    }
}
