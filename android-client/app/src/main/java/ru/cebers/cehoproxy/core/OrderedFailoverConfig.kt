package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject

object OrderedFailoverConfig {
    const val GLOBAL_TAG = "ceho-auto"
    const val PROBE_TAG = "ceho-probe"
    const val LATENCY_TAG = "ceho-latency"
    const val LATENCY_URL = "https://www.gstatic.com/generate_204"
    const val INBOUND_TAG = "ceho-probe-in"

    fun selector(tag: String, candidates: List<String>): JSONObject = JSONObject()
        .put("type", "selector").put("tag", tag)
        .put("outbounds", JSONArray(candidates + "block")).put("default", "block")
        .put("interrupt_exist_connections", true)

    fun candidates(config: String): List<String> {
        val outbounds = JSONObject(config).getJSONArray("outbounds")
        val selector = (0 until outbounds.length()).map { outbounds.getJSONObject(it) }
            .firstOrNull { it.optString("tag") == GLOBAL_TAG } ?: return (0 until outbounds.length()).map { outbounds.getJSONObject(it) }
                .filter { it.optString("type") !in setOf("direct", "block", "dns", "selector", "urltest", "loadbalance") }
                .map { it.optString("tag") }.filter(String::isNotBlank)
        val tags = selector.getJSONArray("outbounds")
        return (0 until tags.length()).map(tags::getString).filter { it != "block" }
    }

    fun proxyTags(config: String): List<String> {
        val outbounds = JSONObject(config).getJSONArray("outbounds")
        return (0 until outbounds.length()).map { outbounds.getJSONObject(it) }
            .filter { it.optString("type") !in setOf("direct", "block", "dns", "selector", "urltest", "loadbalance") }
            .map { it.optString("tag") }.filter(String::isNotBlank)
    }

    fun withProbe(config: String, port: Int): String {
        require(port in 1..65535)
        val root = JSONObject(config)
        val candidates = proxyTags(config)
        if (candidates.isEmpty()) return config
        val inbounds = root.getJSONArray("inbounds")
        require((0 until inbounds.length()).none { inbounds.getJSONObject(it).optString("tag") == INBOUND_TAG })
        inbounds.put(JSONObject().put("type", "mixed").put("tag", INBOUND_TAG)
            .put("listen", "127.0.0.1").put("listen_port", port))
        val outbounds = root.getJSONArray("outbounds")
        require((0 until outbounds.length()).none { outbounds.getJSONObject(it).optString("tag") == PROBE_TAG })
        require((0 until outbounds.length()).map { outbounds.getJSONObject(it) }
            .none { it.optString("tag") == "block" && it.optString("type") != "block" }) {
            "Тег block импортированного профиля должен блокировать трафик"
        }
        if ((0 until outbounds.length()).none { outbounds.getJSONObject(it).optString("tag") == "block" }) {
            outbounds.put(JSONObject().put("type", "block").put("tag", "block"))
        }
        outbounds.put(selector(PROBE_TAG, candidates))
        val route = root.getJSONObject("route")
        val oldRules = route.optJSONArray("rules") ?: JSONArray()
        val rules = JSONArray().put(JSONObject().put("inbound", JSONArray().put(INBOUND_TAG)).put("outbound", PROBE_TAG))
        for (index in 0 until oldRules.length()) rules.put(oldRules.get(index))
        route.put("rules", rules)
        return root.toString(2)
    }
}
