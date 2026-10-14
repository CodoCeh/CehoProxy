package ru.cebers.cehoproxy.core

internal object SiteListInput {
    private val separators = Regex("[,;\\s]+")
    private val hostPattern = Regex("^(?:[\\p{L}\\p{N}](?:[\\p{L}\\p{N}-]{0,61}[\\p{L}\\p{N}])?\\.)+\\p{L}{2,63}$")

    data class Parsed(val hosts: List<String>, val rejected: List<String>)

    fun parse(raw: String): Parsed {
        val hosts = linkedSetOf<String>()
        val rejected = mutableListOf<String>()
        raw.split(separators).map(String::trim).filter(String::isNotEmpty).forEach { token ->
            val host = RoutingConfig.normalizeDomain(token)
            if (hostPattern.matches(host)) hosts += host else rejected += token
        }
        return Parsed(hosts.toList(), rejected)
    }
}
