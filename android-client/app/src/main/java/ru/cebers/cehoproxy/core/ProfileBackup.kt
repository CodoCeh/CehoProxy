package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject

enum class ProfileExportScope(val fileName: String, val title: String) {
    SETTINGS("cehoproxy-settings.json", "Только настройки"),
    SUBSCRIPTIONS("cehoproxy-subscriptions.json", "Только подписки"),
    ALL("cehoproxy-backup.json", "Всё вместе"),
}

object ProfileBackup {
    const val FORMAT = "cehoproxy-android-backup-v1"

    fun encode(profile: ClientProfile, scope: ProfileExportScope = ProfileExportScope.ALL): String {
        val selected = when (scope) {
            ProfileExportScope.SETTINGS -> profile.copy(nodes = emptyList(), selected = 0)
            ProfileExportScope.SUBSCRIPTIONS -> ClientProfile(nodes = profile.nodes, selected = profile.selected)
            ProfileExportScope.ALL -> profile
        }
        return JSONObject()
            .put("format", FORMAT)
            .put("scope", scope.name)
            .put("profile", JSONObject(ProfileCodec.encode(selected)))
            .toString(2)
    }

    fun decode(value: String): ClientProfile? = runCatching {
        val json = JSONObject(value)
        if (json.optString("format") != FORMAT) return null
        json.optJSONObject("profile")?.let { ProfileCodec.decode(it.toString()) } ?: decodeLegacy(json)
    }.getOrNull()

    fun restore(current: ClientProfile, value: String): ClientProfile? = runCatching {
        val json = JSONObject(value)
        if (json.optString("format") != FORMAT) return null
        val incoming = decode(value) ?: return null
        when (runCatching { ProfileExportScope.valueOf(json.optString("scope", "ALL")) }.getOrDefault(ProfileExportScope.ALL)) {
            ProfileExportScope.SETTINGS -> current.copy(
                apps = incoming.apps,
                sites = incoming.sites,
                directCountryCode = incoming.directCountryCode,
                localDpiApps = incoming.localDpiApps,
                onlySites = incoming.onlySites,
                excludeSelectedApps = incoming.excludeSelectedApps,
                importedConfig = incoming.importedConfig,
                expiryRemindersEnabled = incoming.expiryRemindersEnabled,
            )
            ProfileExportScope.SUBSCRIPTIONS -> current.copy(nodes = incoming.nodes, selected = incoming.selected.coerceAtMost((incoming.nodes.size - 1).coerceAtLeast(0)))
            ProfileExportScope.ALL -> incoming
        }
    }.getOrNull()

    private fun decodeLegacy(json: JSONObject): ClientProfile {
        val nodes = json.optJSONArray("nodes")?.let { array ->
            (0 until array.length()).mapNotNull { index ->
                val node = array.optJSONObject(index) ?: return@mapNotNull null
                val raw = node.optString("raw").takeIf(String::isNotBlank) ?: return@mapNotNull null
                val host = node.optString("server").takeIf(String::isNotBlank) ?: return@mapNotNull null
                val port = node.optInt("port", -1).takeIf { it in 1..65535 } ?: return@mapNotNull null
                ProxyNode(node.optString("name", host), node.optString("type", ""), host, port, raw)
            }
        } ?: NodeShareParser.parse(json.optString("subscription", ""))
        return ClientProfile(
            nodes = nodes,
            selected = json.optInt("selected", 0).coerceIn(0, (nodes.size - 1).coerceAtLeast(0)),
            apps = json.array("apps"),
            sites = json.array("sites"),
            onlySites = json.optBoolean("onlySites"),
            excludeSelectedApps = json.optBoolean("excludeSelectedApps"),
            importedConfig = json.optString("config").takeIf(String::isNotBlank),
        )
    }

    private fun JSONObject.array(name: String): List<String> = optJSONArray(name)?.let { array ->
        (0 until array.length()).mapNotNull { index -> array.optString(index).takeIf(String::isNotBlank) }
    }.orEmpty()
}
