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
import ru.cebers.cehoproxy.core.ProfileStore
import ru.cebers.cehoproxy.core.SiteFailurePolicy
import ru.cebers.cehoproxy.core.SiteHintPolicy
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.Executors

/** Watches finished connections; a site that keeps failing through the tunnel but answers directly is offered for the exceptions list. */
internal class SiteWatch(private val context: Context, private val directOpen: () -> Boolean) {
    private val policy = SiteFailurePolicy()
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
                if (host.isEmpty() || connection.outboundType in setOf("direct", "block", "dns")) continue
                val failed = SiteHintPolicy.failed(connection.outboundType.orEmpty(), connection.downlinkTotal, connection.uplinkTotal)
                if (!failed && connection.downlinkTotal == 0L) continue
                val site = policy.record(host, !failed, System.currentTimeMillis()) ?: continue
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
    }

    private fun offer(site: String) {
        if (stopped || !directOpen()) return
        val profile = ProfileStore(context).read()
        if (!SiteHintPolicy.applies(profile.onlySites, profile.sites, site)) return
        val now = System.currentTimeMillis()
        if (!SiteHintPolicy.due(hints.snoozedUntil(site), hints.lastOffer(), now)) return
        if (!reachesDirect(site)) return
        hints.offered(site, now)
        notifyOffer(context, site)
    }

    private fun reachesDirect(site: String): Boolean = try {
        Socket().use { it.connect(InetSocketAddress(site, 443), DIRECT_TIMEOUT_MILLIS) }
        true
    } catch (_: Exception) {
        try { Socket().use { it.connect(InetSocketAddress("www.$site", 443), DIRECT_TIMEOUT_MILLIS) }; true } catch (_: Exception) { false }
    }

    companion object {
        const val ACTION_EXCLUDE_SITE = "ru.cebers.cehoproxy.EXCLUDE_SITE"
        const val ACTION_HINT_NEVER = "ru.cebers.cehoproxy.HINT_NEVER"
        const val EXTRA_SITE = "site"
        const val SITE_CHANNEL = "cehoproxy-site"
        private const val DIRECT_TIMEOUT_MILLIS = 2_500
        private const val NOTIFICATION_BASE = 100

        fun notificationId(site: String): Int = NOTIFICATION_BASE + (site.hashCode() and 0x7fff)

        fun cancel(context: Context, site: String) =
            context.getSystemService(NotificationManager::class.java).cancel(notificationId(site))

        private fun notifyOffer(context: Context, site: String) {
            CehoVpnService.ensureChannels(context)
            fun action(name: String) = PendingIntent.getService(
                context, notificationId(site) + name.hashCode() and 0xffff,
                Intent(context, CehoVpnService::class.java).setAction(name).putExtra(EXTRA_SITE, site),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val text = "Сайт не отвечает через VPN, а напрямую открывается. Открывать его мимо VPN?"
            val note = NotificationCompat.Builder(context, SITE_CHANNEL).setSmallIcon(ru.cebers.cehoproxy.R.drawable.ic_ceho_whirlpool)
                .setContentTitle("$site не открывается").setContentText(text).setStyle(NotificationCompat.BigTextStyle().bigText(text))
                .setCategory(NotificationCompat.CATEGORY_RECOMMENDATION).setPriority(NotificationCompat.PRIORITY_DEFAULT)
                .setAutoCancel(true).setOnlyAlertOnce(true)
                .addAction(0, "Открывать напрямую", action(ACTION_EXCLUDE_SITE))
                .addAction(0, "Не предлагать", action(ACTION_HINT_NEVER))
                .build()
            context.getSystemService(NotificationManager::class.java).notify(notificationId(site), note)
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
