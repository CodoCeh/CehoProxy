package ru.cebers.cehoproxy

import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import io.nekohasekai.libbox.Libbox
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.NodeShareParser
import ru.cebers.cehoproxy.core.ProfileStore
import ru.cebers.cehoproxy.core.ProxyNode
import ru.cebers.cehoproxy.core.RoutePolicy
import ru.cebers.cehoproxy.core.RoutingConfig
import ru.cebers.cehoproxy.core.SubscriptionFetcher
import ru.cebers.cehoproxy.core.SubscriptionNames
import ru.cebers.cehoproxy.core.SubscriptionRefreshPolicy
import ru.cebers.cehoproxy.core.SubscriptionRequestOptions
import java.util.concurrent.TimeUnit

object SubscriptionAutoRefresh {
    private const val PREFS = "subscription_refresh"
    private const val KEY_SCHEDULE = "schedule"
    private const val WORK_NAME = "subscription-refresh"

    /** Enqueues the periodic work only when the interval or the subscription URL changes. */
    fun reconcile(context: Context, profile: ClientProfile) {
        val hours = profile.subscriptionRefreshHours
        val url = profile.subscriptionUrl
        val enabled = hours > 0 && !url.isNullOrBlank() && profile.importedConfig == null
        val schedule = if (enabled) "$hours|$url" else ""
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getString(KEY_SCHEDULE, "") == schedule) return
        val work = WorkManager.getInstance(context)
        prefs.edit().putString(KEY_SCHEDULE, schedule).apply()
        if (!enabled) {
            work.cancelUniqueWork(WORK_NAME)
            return
        }
        val request = PeriodicWorkRequestBuilder<SubscriptionRefreshWorker>(hours.toLong(), TimeUnit.HOURS)
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        work.enqueueUniquePeriodicWork(WORK_NAME, ExistingPeriodicWorkPolicy.UPDATE, request)
    }

    /** Downloads the subscription and stores the nodes. Returns false when the provider is unreachable or sent no nodes. */
    fun refreshNow(context: Context): Boolean {
        val store = ProfileStore(context)
        val profile = store.read()
        if (profile.importedConfig != null) return false
        val fetched = fetchAll(profile.subscriptionUrls)
        if (fetched.isEmpty()) return false
        store.update { current -> SubscriptionRefreshPolicy.applyFetchedSources(current, fetched, System.currentTimeMillis()) }
        return true
    }

    fun fetchAll(urls: List<String>): Map<String, List<ProxyNode>> =
        urls.mapNotNull { url -> fetchNodes(url)?.takeIf { it.isNotEmpty() }?.let { url to it } }.toMap()

    fun fetchNodes(url: String): List<ProxyNode>? = runCatching {
        val fetched = SubscriptionFetcher.fetchWithMetadata(url, SubscriptionRequestOptions())
        val (valid, _) = validateNodes(SubscriptionNames.resolve(url, NodeShareParser.parse(fetched.body)))
        fetched.metadata.attachTo(valid)
    }.getOrNull()
}

class SubscriptionRefreshWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result {
        withContext(Dispatchers.IO) { SubscriptionAutoRefresh.refreshNow(applicationContext) }
        return Result.success()
    }
}

internal fun validateNodes(nodes: List<ProxyNode>): Pair<List<ProxyNode>, Int> {
    val valid = nodes.filter { node -> runCatching { Libbox.checkConfig(RoutingConfig.build(node, RoutePolicy())) }.isSuccess }
    return valid to (nodes.size - valid.size)
}
