package ru.cebers.cehoproxy.vpn

/** Tests subscription order and never interprets latency as priority. */
internal object OrderedFailover {
    fun firstHealthy(candidates: List<String>, isCurrent: () -> Boolean, probe: (String) -> Boolean): String? {
        for (candidate in candidates) {
            if (!isCurrent()) return null
            if (probe(candidate) && isCurrent()) return candidate
        }
        return null
    }

    /**
     * Picks a node that answers now. Nodes that failed recently are tried last, and nodes that answer slower
     * than [slowMillis] are used only when no quicker node answers.
     */
    fun firstWorking(candidates: List<String>, isCurrent: () -> Boolean, memory: NodeHealthMemory, slowMillis: Long, probe: (String) -> Long?): String? {
        var slow: String? = null
        for (candidate in candidates.filterNot(memory::isBad) + candidates.filter(memory::isBad)) {
            if (!isCurrent()) return null
            val answeredIn = probe(candidate)
            if (answeredIn == null) { memory.markBad(candidate); continue }
            memory.markGood(candidate)
            if (answeredIn <= slowMillis) return candidate.takeIf { isCurrent() }
            if (slow == null) slow = candidate
        }
        return slow?.takeIf { isCurrent() }
    }
}

/** Remembers which nodes just failed, so the next switch does not land on them again. */
internal class NodeHealthMemory(private val now: () -> Long = System::currentTimeMillis) {
    private val badUntil = java.util.concurrent.ConcurrentHashMap<String, Long>()
    fun markBad(tag: String) { badUntil[tag] = now() + BAD_MILLIS }
    fun markGood(tag: String) { badUntil.remove(tag) }
    fun isBad(tag: String): Boolean = (badUntil[tag] ?: 0L) > now()
    fun clear() = badUntil.clear()

    companion object { const val BAD_MILLIS = 180_000L }
}
