package ru.cebers.cehoproxy.core

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

data class ClientProfile(
    val nodes: List<ProxyNode> = emptyList(),
    val selected: Int = 0,
    val apps: List<String> = emptyList(),
    val sites: List<String> = emptyList(),
    val directCountryCode: String = "",
    val localDpiApps: List<String> = emptyList(),
    val onlySites: Boolean = false,
    val importedConfig: String? = null,
    val excludeSelectedApps: Boolean = false,
    val expiryRemindersEnabled: Boolean = false,
    val appNodeRoutes: Map<String, String> = emptyMap(),
    val siteNodeRoutes: Map<String, String> = emptyMap(),
    val autoRotationEnabled: Boolean = false,
    val autoRotationIntervalSeconds: Int = 900,
    val aiMode: Boolean = false,
    val autoExcludedRouteIds: List<String> = emptyList(),
    val excludedCountriesBySubscription: Map<String, List<String>> = emptyMap(),
    val subscriptionUrls: List<String> = emptyList(),
    val subscriptionFetchedAt: Long = 0L,
    val subscriptionFetchedAtByUrl: Map<String, Long> = emptyMap(),
    val subscriptionRefreshHours: Int = SubscriptionRefreshPolicy.DEFAULT_INTERVAL_HOURS,
    val blockYoutubeAds: Boolean = false,
    val disabledSubscriptions: List<String> = emptyList(),
    val autoDisabledSubscriptions: List<String> = emptyList(),
) {
    val selectedNode: ProxyNode? get() = nodes.getOrNull(selected)
    val subscriptionUrl: String? get() = subscriptionUrls.firstOrNull()
}

// First run: everything goes through VPN and the best server is chosen automatically, so the user has nothing to configure.
fun newInstallProfile(): ClientProfile = ClientProfile(excludeSelectedApps = true, autoRotationEnabled = true)

val ClientProfile.excludedCountrySets: Map<String, Set<String>>
    get() = excludedCountriesBySubscription.mapValues { (_, countries) -> countries.map { it.uppercase() }.toSet() }

object ProfileCodec {
    fun encode(profile: ClientProfile): String {
        val nodes = JSONArray()
        profile.nodes.forEach { node ->
            val metadata = node.subscriptionMetadata
            nodes.put(JSONObject().put("name", node.name).put("type", node.type).put("server", node.server).put("port", node.port).put("raw", node.raw)
                .put("subscriptionExpiresAt", node.subscriptionExpiresAt ?: metadata.expiresAtEpochSeconds ?: 0L)
                .put("subscriptionMetadata", JSONObject().put("upload", metadata.uploadBytes ?: -1L).put("download", metadata.downloadBytes ?: -1L).put("total", metadata.totalBytes ?: -1L)
                    .put("title", metadata.title ?: "").put("infoText", metadata.infoText ?: "").put("announcement", metadata.announcement ?: "")
                    .put("supportUrl", metadata.supportUrl ?: "").put("webPageUrl", metadata.webPageUrl ?: "").put("infoButtonText", metadata.infoButtonText ?: "")
                    .put("infoButtonUrl", metadata.infoButtonUrl ?: "").put("renewUrl", metadata.renewUrl ?: "").put("notifyOnExpiry", metadata.notifyOnExpiry).put("sourceHost", metadata.sourceHost ?: "").put("sourceUrl", metadata.sourceUrl ?: "").put("updateIntervalHours", metadata.updateIntervalHours ?: 0)))
        }
        return JSONObject().put("nodes", nodes)
            .put("subscription", profile.nodes.joinToString("\n") { it.raw })
            .put("selected", profile.selected).put("apps", JSONArray(profile.apps)).put("sites", JSONArray(profile.sites))
            .put("directCountryCode", CountryRouting.normalize(profile.directCountryCode))
            .put("localDpiApps", JSONArray(LocalDpiPolicy.normalize(profile.localDpiApps)))
            .put("onlySites", profile.onlySites).put("config", profile.importedConfig ?: "")
            .put("excludeSelectedApps", profile.excludeSelectedApps).put("expiryRemindersEnabled", profile.expiryRemindersEnabled)
            .put("appNodeRoutes", JSONObject(profile.appNodeRoutes)).put("siteNodeRoutes", JSONObject(profile.siteNodeRoutes))
            .put("aiMode", profile.aiMode).put("autoRotationEnabled", profile.autoRotationEnabled).put("autoRotationIntervalSeconds", profile.autoRotationIntervalSeconds.coerceIn(60, 3600))
            .put("autoExcludedRouteIds", JSONArray(profile.autoExcludedRouteIds.distinct()))
            .put("excludedCountriesBySubscription", JSONObject().apply { profile.excludedCountriesBySubscription.forEach { (key, countries) -> put(key, JSONArray(countries.map { it.uppercase() }.distinct())) } })
            .put("subscriptionUrls", JSONArray(profile.subscriptionUrls)).put("subscriptionFetchedAt", profile.subscriptionFetchedAt)
            .put("subscriptionFetchedAtByUrl", JSONObject().apply { profile.subscriptionFetchedAtByUrl.forEach { (url, at) -> put(url, at) } })
            .put("subscriptionRefreshHours", profile.subscriptionRefreshHours).put("blockYoutubeAds", profile.blockYoutubeAds).put("disabledSubscriptions", JSONArray(profile.disabledSubscriptions.distinct())).put("autoDisabledSubscriptions", JSONArray(profile.autoDisabledSubscriptions.distinct())).toString()
    }

    fun decode(encoded: String): ClientProfile = runCatching {
        val json = JSONObject(encoded)
        val storedNodes = json.optJSONArray("nodes")
        val nodes = if (storedNodes != null) {
            (0 until storedNodes.length()).mapNotNull { index ->
                storedNodes.optJSONObject(index)?.let { node ->
                    val raw = node.optString("raw")
                    val server = node.optString("server")
                    val port = node.optInt("port")
                    if (raw.isBlank() || server.isBlank() || port !in 1..65535) null
                    else {
                        val expires = node.optLong("subscriptionExpiresAt").takeIf { it > 0 }
                        val m = node.optJSONObject("subscriptionMetadata") ?: JSONObject()
                        val metadata = SubscriptionMetadata(expires, m.optLong("upload", -1).takeIf { it >= 0 }, m.optLong("download", -1).takeIf { it >= 0 }, m.optLong("total", -1).takeIf { it >= 0 },
                            m.optString("title").takeIf(String::isNotBlank), m.optString("infoText").takeIf(String::isNotBlank), m.optString("announcement").takeIf(String::isNotBlank),
                            m.optString("supportUrl").takeIf(String::isNotBlank), m.optString("webPageUrl").takeIf(String::isNotBlank), m.optString("infoButtonText").takeIf(String::isNotBlank),
                            m.optString("infoButtonUrl").takeIf(String::isNotBlank), m.optString("renewUrl").takeIf(String::isNotBlank), m.optBoolean("notifyOnExpiry"), m.optString("sourceHost").takeIf(String::isNotBlank), m.optString("sourceUrl").takeIf(String::isNotBlank), m.optInt("updateIntervalHours").takeIf { it > 0 })
                        ProxyNode(node.optString("name", server), node.optString("type", ""), server, port, raw, expires, metadata)
                    }
                }
            }
        } else NodeShareParser.parse(json.optString("subscription", ""))
        ClientProfile(
            nodes = nodes,
            selected = json.optInt("selected", 0).coerceIn(0, (nodes.size - 1).coerceAtLeast(0)),
            apps = json.array("apps"),
            sites = json.array("sites"),
            directCountryCode = CountryRouting.normalize(json.optString("directCountryCode")),
            localDpiApps = LocalDpiPolicy.normalize(json.array("localDpiApps")),
            onlySites = json.optBoolean("onlySites"),
            importedConfig = json.optString("config").takeIf(String::isNotBlank),
            excludeSelectedApps = json.optBoolean("excludeSelectedApps"),
            expiryRemindersEnabled = json.optBoolean("expiryRemindersEnabled"),
            appNodeRoutes = json.objectMap("appNodeRoutes"),
            siteNodeRoutes = json.objectMap("siteNodeRoutes"),
            autoRotationEnabled = json.optBoolean("autoRotationEnabled"),
            aiMode = json.optBoolean("aiMode"),
            autoRotationIntervalSeconds = json.optInt("autoRotationIntervalSeconds", 900).coerceIn(60, 3600),
            autoExcludedRouteIds = json.array("autoExcludedRouteIds"),
            excludedCountriesBySubscription = json.countriesBySubscription(nodes),
            subscriptionUrls = json.array("subscriptionUrls").ifEmpty { json.optString("subscriptionUrl").takeIf(String::isNotBlank)?.let { listOf(it) }.orEmpty() },
            subscriptionFetchedAt = json.optLong("subscriptionFetchedAt"),
            subscriptionFetchedAtByUrl = json.optJSONObject("subscriptionFetchedAtByUrl")?.let { obj -> obj.keys().asSequence().associateWith { obj.optLong(it) } }.orEmpty(),
            subscriptionRefreshHours = json.optInt("subscriptionRefreshHours", SubscriptionRefreshPolicy.DEFAULT_INTERVAL_HOURS)
                .takeIf { it in SubscriptionRefreshPolicy.INTERVAL_OPTIONS_HOURS } ?: SubscriptionRefreshPolicy.DEFAULT_INTERVAL_HOURS,
            blockYoutubeAds = json.optBoolean("blockYoutubeAds"),
            disabledSubscriptions = json.array("disabledSubscriptions"),
            autoDisabledSubscriptions = json.array("autoDisabledSubscriptions"),
        )
    }.getOrDefault(ClientProfile())

    // Profiles saved before per-subscription exclusions kept one country list for all subscriptions; it is applied to each of them.
    private fun JSONObject.countriesBySubscription(nodes: List<ProxyNode>): Map<String, List<String>> {
        optJSONObject("excludedCountriesBySubscription")?.let { obj ->
            return obj.keys().asSequence().associateWith { key -> obj.optJSONArray(key)?.let { array -> (0 until array.length()).map { array.getString(it).uppercase() } }.orEmpty() }
                .filterValues { it.isNotEmpty() }
        }
        val legacy = array("excludedCountries").map { it.uppercase() }
        if (legacy.isEmpty()) return emptyMap()
        return nodes.map { NodePresentation.subscriptionKey(it) }.distinct().associateWith { legacy }
    }

    private fun JSONObject.array(name: String): List<String> = optJSONArray(name)?.let { array -> (0 until array.length()).map(array::getString) }.orEmpty()
    private fun JSONObject.objectMap(name: String): Map<String, String> = optJSONObject(name)?.let { obj ->
        obj.keys().asSequence().mapNotNull { key -> obj.optString(key).takeIf(String::isNotBlank)?.let { key to it } }.toMap()
    }.orEmpty()
}

object ProfileImportPolicy {
    fun useSingBoxConfig(profile: ClientProfile, config: String): ClientProfile {
        val importedNodes = NodeShareParser.parse(config).ifEmpty { profile.nodes }
        return profile.copy(
            nodes = importedNodes,
            selected = profile.selected.coerceIn(0, (importedNodes.size - 1).coerceAtLeast(0)),
            sites = emptyList(),
            onlySites = false,
            importedConfig = config,
        )
    }
}

object VpnStatusPolicy {
    fun isConnected(storedStatus: String?, liveServiceConnected: Boolean): Boolean =
        storedStatus == "connected" && liveServiceConnected

    fun isStarting(storedStatus: String?, liveServiceInstance: Boolean): Boolean =
        storedStatus == "starting" && liveServiceInstance
}

class ProfileStore(context: Context) {
    companion object {
        // UI and VPN use separate store instances for the same preferences.
        private val lock = Any()
    }

    private val prefs = context.getSharedPreferences("profile", Context.MODE_PRIVATE)
    fun read(): ClientProfile = synchronized(lock) { readLocked() }
    fun save(profile: ClientProfile) = synchronized(lock) { saveLocked(profile) }

    fun update(transform: (ClientProfile) -> ClientProfile): ClientProfile = synchronized(lock) {
        val current = readLocked()
        val updated = transform(current)
        if (updated != current) saveLocked(updated)
        updated
    }

    private fun readLocked(): ClientProfile = prefs.getString("data", null)?.let(ProfileCodec::decode) ?: newInstallProfile()
    private fun saveLocked(profile: ClientProfile) { prefs.edit().putString("data", ProfileCodec.encode(profile)).apply() }
}
