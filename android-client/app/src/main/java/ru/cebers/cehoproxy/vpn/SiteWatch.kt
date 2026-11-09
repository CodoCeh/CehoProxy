package ru.cebers.cehoproxy.vpn

import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import io.nekohasekai.libbox.CommandClient
import io.nekohasekai.libbox.CommandClientHandler
import io.nekohasekai.libbox.CommandClientOptions
import io.nekohasekai.libbox.ConnectionEvents
import io.nekohasekai.libbox.Libbox
import io.nekohasekai.libbox.LogIterator
import io.nekohasekai.libbox.OutboundGroupItemIterator
import io.nekohasekai.libbox.OutboundGroupIterator
import io.nekohasekai.libbox.StatusMessage
import io.nekohasekai.libbox.StringIterator
import ru.cebers.cehoproxy.core.AppFailurePolicy
import ru.cebers.cehoproxy.core.BypassOffer
import ru.cebers.cehoproxy.core.ProfileStore
import ru.cebers.cehoproxy.core.SiteFailurePolicy
import ru.cebers.cehoproxy.core.SiteHintPolicy
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.Executors

/** Watches finished connections; a site that keeps failing through the tunnel but answers directly is offered for the exceptions list. */
internal class SiteWatch(private val context: Context, private val directOpen: () -> Boolean) {
    private val policy = SiteFailurePolicy()
    private val appPolicy = AppFailurePolicy()
    private val directPolicy = SiteFailurePolicy()
    private val hints = SiteHintStore(context)
    private val executor = Executors.newSingleThreadExecutor { task -> Thread(task, "ceho-site-watch").apply { isDaemon = true } }
    @Volatile private var client: CommandClient? = null
    @Volatile private var stopped = false

    private val handler = object : CommandClientHandler {
        override fun clearLogs() = Unit
        override fun connected() = Unit
        override fun disconnected(message: String?) = Unit
        override fun initializeClashMode(modes: StringIterator?, current: String?) = Unit
        override fun setDefaultLogLevel(level: Int) = Unit
        override fun updateClashMode(mode: String?) = Unit
        override fun writeGroups(groups: OutboundGroupIterator?) = Unit
        override fun writeLogs(logs: LogIterator?) = Unit
        override fun writeOutbounds(items: OutboundGroupItemIterator?) = Unit
        override fun writeStatus(status: StatusMessage?) = Unit
        override fun writeConnectionEvents(events: ConnectionEvents?) {
            if (stopped || events == null || events.reset) return
            val list = events.iterator()
            while (list.hasNext()) {
                val event = list.next()
                if (event.type.toLong() != Libbox.ConnectionEventClosed) continue
                val connection = event.connection ?: continue
                val host = connection.domain.orEmpty()
                val type = connection.outboundType.orEmpty()
                if (host.isEmpty() || !connection.network.equals("tcp", ignoreCase = true) || type in setOf("block", "dns")) continue
                val now = System.currentTimeMillis()
                if (type == "direct") {
                    if (SiteHintPolicy.failedDirect(type, connection.downlinkTotal, connection.uplinkTotal)) {
                        directPolicy.record(host, false, now)?.let { site -> runCatching { executor.execute { offerTunnel(site) } } }
                    } else if (connection.downlinkTotal > 0L) directPolicy.record(host, true, now)
                    continue
                }
                val failed = SiteHintPolicy.failed(type, connection.downlinkTotal, connection.uplinkTotal)
                if (!failed && connection.downlinkTotal == 0L) continue
                if (!failed) directPolicy.record(host, true, now)
                val owner = runCatching { connection.processInfo?.userName }.getOrNull().orEmpty()
                    .takeIf { it != context.packageName && it != "android" && it != "root" }.orEmpty()
                if (owner.isNotEmpty()) appPolicy.record(owner, host, !failed, now)?.let { app -> runCatching { executor.execute { offerApp(app) } } }
                val site = policy.record(host, !failed, now) ?: continue
                runCatching { executor.execute { offer(site) } }
            }
        }
    }

    fun start() {
        runCatching {
            executor.execute {
                if (stopped) return@execute
                runCatching {
                    val options = CommandClientOptions().apply { addCommand(Libbox.CommandConnections); statusInterval = 2_000_000_000L }
                    val created = CommandClient(handler, options)
                    client = created
                    created.connect()
                }
            }
        }
    }

    fun stop() {
        stopped = true
        val current = client
        client = null
        runCatching { executor.execute { runCatching { current?.disconnect() } } }
        runCatching { executor.shutdown() }
        policy.reset()
        appPolicy.reset()
        directPolicy.reset()
    }

    private fun offer(site: String) {
        if (stopped || !directOpen()) return
        val profile = ProfileStore(context).read()
        if (!SiteHintPolicy.applies(profile.onlySites, profile.sites, site)) return
        val now = System.currentTimeMillis()
        if (!SiteHintPolicy.due(hints.snoozedUntil(site), hints.lastOffer(), now)) return
        if (!reachesDirect(site)) return
        hints.offered(site, now)
        notifyOffer(context, BypassOffer.Kind.SITE, site, site)
    }

    private fun offerTunnel(site: String) {
        if (stopped || !directOpen()) return
        val profile = ProfileStore(context).read()
        if (!SiteHintPolicy.tunnelApplies(profile.onlySites, profile.sites, profile.siteNodeRoutes.keys, profile.nodes.isNotEmpty(), site)) return
        val key = BypassOffer.hintKey(BypassOffer.Kind.TUNNEL, site)
        val now = System.currentTimeMillis()
        if (!SiteHintPolicy.due(hints.snoozedUntil(key), hints.lastOffer(), now)) return
        hints.offered(key, now)
        notifyOffer(context, BypassOffer.Kind.TUNNEL, site, site)
    }

    private fun offerApp(app: AppFailurePolicy.Failure) {
        if (stopped || !directOpen()) return
        val profile = ProfileStore(context).read()
        if (!SiteHintPolicy.appApplies(profile.excludeSelectedApps, profile.apps, app.packageName)) return
        val key = "app:${app.packageName}"
        val now = System.currentTimeMillis()
        if (!SiteHintPolicy.due(hints.snoozedUntil(key), hints.lastOffer(), now)) return
        if (app.sites.none { reachesDirect(it) }) return
        val label = runCatching {
            context.packageManager.getApplicationLabel(context.packageManager.getApplicationInfo(app.packageName, 0)).toString()
        }.getOrDefault(app.packageName)
        hints.offered(key, now)
        notifyOffer(context, BypassOffer.Kind.APP, app.packageName, label)
    }

    private fun reachesDirect(site: String): Boolean = try {
        Socket().use { it.connect(InetSocketAddress(site, 443), DIRECT_TIMEOUT_MILLIS) }
        true
    } catch (_: Exception) {
        try { Socket().use { it.connect(InetSocketAddress("www.$site", 443), DIRECT_TIMEOUT_MILLIS) }; true } catch (_: Exception) { false }
    }

    companion object {
        const val ACTION_EXCLUDE_SITE = "ru.cebers.cehoproxy.EXCLUDE_SITE"
        const val ACTION_TUNNEL_SITE = "ru.cebers.cehoproxy.TUNNEL_SITE"
        const val ACTION_EXCLUDE_APP = "ru.cebers.cehoproxy.EXCLUDE_APP"
        const val ACTION_HINT_NEVER = "ru.cebers.cehoproxy.HINT_NEVER"
        const val ACTION_BYPASS_OFFER = "ru.cebers.cehoproxy.BYPASS_OFFER"
        const val EXTRA_KIND = "kind"
        const val EXTRA_TARGET = "target"
        const val EXTRA_LABEL = "label"
        const val SITE_CHANNEL = "cehoproxy-site"
        private const val DIRECT_TIMEOUT_MILLIS = 2_500
        private const val NOTIFICATION_BASE = 100

        fun notificationId(key: String): Int = NOTIFICATION_BASE + (key.hashCode() and 0x7fff)

        fun cancel(context: Context, kind: BypassOffer.Kind, target: String) =
            context.getSystemService(NotificationManager::class.java).cancel(notificationId(BypassOffer.hintKey(kind, target)))

        private fun notifyOffer(context: Context, kind: BypassOffer.Kind, target: String, label: String) {
            CehoVpnService.ensureChannels(context)
            val id = notificationId(BypassOffer.hintKey(kind, target))
            val text = BypassOffer.text(kind, label)
            fun service(name: String) = PendingIntent.getService(
                context, id + name.hashCode() and 0xffff,
                Intent(context, CehoVpnService::class.java).setAction(name).putExtra(EXTRA_KIND, kind.wire).putExtra(EXTRA_TARGET, target),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val open = PendingIntent.getActivity(
                context, id,
                Intent(context, ru.cebers.cehoproxy.MainActivity::class.java).setAction(ACTION_BYPASS_OFFER)
                    .putExtra(EXTRA_KIND, kind.wire).putExtra(EXTRA_TARGET, target).putExtra(EXTRA_LABEL, label)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val confirm = when (kind) { BypassOffer.Kind.APP -> ACTION_EXCLUDE_APP; BypassOffer.Kind.TUNNEL -> ACTION_TUNNEL_SITE; BypassOffer.Kind.SITE -> ACTION_EXCLUDE_SITE }
            val sender = androidx.core.app.Person.Builder().setName("CehoProxy").setImportant(true).build()
            val message = NotificationCompat.MessagingStyle(sender).setConversationTitle(text.title)
                .addMessage(text.title + "\n" + text.body, System.currentTimeMillis(), sender)
            val note = NotificationCompat.Builder(context, SITE_CHANNEL).setSmallIcon(ru.cebers.cehoproxy.R.drawable.ic_ceho_whirlpool)
                .setContentTitle(text.title).setContentText(text.body).setStyle(message)
                .setCategory(NotificationCompat.CATEGORY_MESSAGE).setPriority(NotificationCompat.PRIORITY_HIGH).setDefaults(NotificationCompat.DEFAULT_ALL)
                .setVisibility(NotificationCompat.VISIBILITY_PUBLIC).setWhen(System.currentTimeMillis()).setShowWhen(true)
                .setAutoCancel(true).setOnlyAlertOnce(true).setContentIntent(open)
                .addAction(0, text.confirm, service(confirm))
                .addAction(0, text.never, service(ACTION_HINT_NEVER))
                .build()
            context.getSystemService(NotificationManager::class.java).notify(id, note)
        }
    }
}

/** Remembers which sites were already offered, so the same site does not ask again and again. */
internal class SiteHintStore(context: Context) {
    private val prefs = context.getSharedPreferences("site_hints", Context.MODE_PRIVATE)

    fun snoozedUntil(site: String): Long = prefs.getLong("snooze:$site", 0L)
    fun lastOffer(): Long = prefs.getLong("last", 0L)
    fun offered(site: String, now: Long) {
        prefs.edit().putLong("snooze:$site", now + SiteHintPolicy.SNOOZE_MILLIS).putLong("last", now).apply()
    }
    fun never(site: String) { prefs.edit().putLong("snooze:$site", SiteHintPolicy.NEVER).apply() }
}
