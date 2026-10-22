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
import ru.cebers.cehoproxy.core.EventLog
import ru.cebers.cehoproxy.core.NodeShareParser
import ru.cebers.cehoproxy.core.ProfileStore
import ru.cebers.cehoproxy.core.ProxyNode
import ru.cebers.cehoproxy.core.RoutePolicy
import ru.cebers.cehoproxy.core.RoutingConfig
import ru.cebers.cehoproxy.core.SubscriptionNames
import ru.cebers.cehoproxy.core.SubscriptionRefreshPolicy
import java.util.concurrent.TimeUnit

object SubscriptionAutoRefresh {
    private const val PREFS = "subscription_refresh"
    private const val KEY_SCHEDULE = "schedule"
    private const val WORK_NAME = "subscription-refresh"

    /** Enqueues the periodic work only when the interval or the subscription URL changes. */
    fun reconcile(context: Context, profile: ClientProfile) {
        val hours = SubscriptionRefreshPolicy.backgroundHours(profile)
        val enabled = hours > 0 && profile.importedConfig == null
        val schedule = if (enabled) "$hours|${profile.subscriptionUrls.joinToString(",")}" else ""
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
        val due = SubscriptionRefreshPolicy.dueUrls(profile, System.currentTimeMillis())
        if (due.isEmpty()) return true
        val fetched = fetchAll(context, due)
        if (fetched.isEmpty()) return false
        apply(context, store, fetched)
        return true
    }

    /** Applies the downloaded nodes to the freshest saved profile and writes down which nodes came and went. */
    fun apply(context: Context, store: ProfileStore, fetched: Map<String, List<ProxyNode>>): ClientProfile {
        var added = emptyList<String>()
        var removed = emptyList<String>()
        val result = store.update { current ->
            SubscriptionRefreshPolicy.applyFetchedSources(current, fetched, System.currentTimeMillis()).also { next ->
                val before = current.nodes.map { it.raw }.toSet()
                val after = next.nodes.map { it.raw }.toSet()
                added = next.nodes.filter { it.raw !in before }.map { it.name }
                removed = current.nodes.filter { it.raw !in after }.map { it.name }
            }
        }
        if (added.isNotEmpty() || removed.isNotEmpty()) EventLog.append(context, "список узлов подписки изменился: добавлено ${added.size}${names(added)}, убрано ${removed.size}${names(removed)}")
        return result
    }

    private fun names(list: List<String>): String = if (list.isEmpty()) "" else list.take(5).joinToString(prefix = " (", postfix = if (list.size > 5) ", …)" else ")")

    /** Subscriptions are downloaded side by side, so one slow provider does not hold up the others. */
    fun fetchAll(context: Context, urls: List<String>): Map<String, List<ProxyNode>> {
        if (urls.isEmpty()) return emptyMap()
        val pool = java.util.concurrent.Executors.newFixedThreadPool(urls.size.coerceAtMost(4))
        return try {
            urls.map { url -> pool.submit(java.util.concurrent.Callable { url to fetchNodes(context, url) }) }
                .mapNotNull { future -> future.get().let { (url, nodes) -> nodes?.takeIf { it.isNotEmpty() }?.let { url to it } } }.toMap()
        } finally { pool.shutdown() }
    }

    fun fetchNodes(context: Context, url: String): List<ProxyNode>? = runCatching {
        val fetched = SubscriptionTunnelFetch.fetch(context, url)
        val parseStarted = System.currentTimeMillis()
        val parsed = SubscriptionNames.resolve(url, NodeShareParser.parse(fetched.body), required = true) { u, o -> SubscriptionTunnelFetch.fetch(context, u, o).body }
        val resolvedAt = System.currentTimeMillis()
        val (valid, rejected) = validateNodes(parsed)
        val validatedAt = System.currentTimeMillis()
        if (valid.isEmpty()) EventLog.append(context, "подписка не дала узлов: разобрано ${parsed.size}, отклонено движком $rejected, размер ответа ${fetched.body.length}")
        EventLog.append(context, "подписка разобрана: узлов ${valid.size} из ${parsed.size}, имена ${resolvedAt - parseStarted} мс, проверка движком ${validatedAt - resolvedAt} мс")
        fetched.metadata.attachTo(valid)
    }.onFailure { error -> EventLog.append(context, "обновление подписки не удалось: ${error.javaClass.simpleName}: ${error.message}") }.getOrNull()
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
