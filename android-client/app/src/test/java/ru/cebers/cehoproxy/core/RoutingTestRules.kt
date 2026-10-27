package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject

/** Route rules without the always-present DNS hijack and IPv6 reject rules, which are not what these tests describe. */
internal fun JSONObject.routeRulesWithoutDefaults(): JSONArray {
    val route = getJSONObject("route").getJSONArray("rules")
    return JSONArray((0 until route.length()).map { route.getJSONObject(it) }.filter { it.optString("action") != "hijack-dns" && !it.has("ip_version") })
}
