package ru.cebers.cehoproxy.core

import android.content.Context
import org.json.JSONObject

internal object NodeCountryCachePolicy {
    const val TTL_MILLIS = 24 * 60 * 60 * 1000L
    fun isFresh(country: VerifiedNodeCountry, now: Long): Boolean = country.checkedAt > 0 && now >= country.checkedAt && now - country.checkedAt < TTL_MILLIS
    fun encode(country: VerifiedNodeCountry): String = JSONObject().put("country", country.countryCode).put("ip", country.exitIp).put("checkedAt", country.checkedAt).toString()
    fun decode(encoded: String?, now: Long): VerifiedNodeCountry? = runCatching {
        val body = encoded ?: return null
        val country = NodeExitCountryProbe.parse(body)?.copy(checkedAt = JSONObject(body).optLong("checkedAt")) ?: return null
        country.takeIf { isFresh(it, now) }
    }.getOrNull()
}

internal class NodeCountryCache(context: Context) {
    private val prefs = context.getSharedPreferences("verified_node_countries", Context.MODE_PRIVATE)
    fun read(routeId: String, now: Long = System.currentTimeMillis()): VerifiedNodeCountry? {
        if (!routeId.matches(Regex("[a-f0-9]{64}"))) return null
        return NodeCountryCachePolicy.decode(prefs.getString(routeId, null), now)
    }
    fun save(routeId: String, country: VerifiedNodeCountry) {
        if (!routeId.matches(Regex("[a-f0-9]{64}"))) return
        prefs.edit().putString(routeId, NodeCountryCachePolicy.encode(country)).apply()
    }
}
