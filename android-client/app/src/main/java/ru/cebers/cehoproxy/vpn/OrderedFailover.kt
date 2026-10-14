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
}
