package ru.cebers.cehoproxy

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.NodePresentation
import ru.cebers.cehoproxy.core.QuotaPolicy

/** Warns once per level (80%, then 95%) and subscription period when the provider reports a traffic limit. */
internal object QuotaWarnings {
    private const val CHANNEL = "cehoproxy-quota"
    private const val PREFS = "quota_warnings"

    fun check(context: Context, profile: ClientProfile) {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        profile.nodes.groupBy(NodePresentation::subscriptionKey).forEach { (key, nodes) ->
            val metadata = nodes.first().subscriptionMetadata
            val total = metadata.totalBytes ?: return@forEach
            val used = metadata.usedBytes ?: return@forEach
            val level = QuotaPolicy.level(used, total)
            val period = "$key|${metadata.expiresAtEpochSeconds ?: 0}"
            val warned = prefs.getInt(period, 0)
            if (level == 0 || level <= warned) return@forEach
            prefs.edit().putInt(period, level).apply()
            notify(context, key.hashCode(), QuotaPolicy.message(metadata.title ?: NodePresentation.subscriptionSource(nodes.first()), used, total))
        }
    }

    private fun notify(context: Context, id: Int, text: String) {
        val manager = context.getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) manager.createNotificationChannel(NotificationChannel(CHANNEL, "Трафик подписки", NotificationManager.IMPORTANCE_DEFAULT))
        val note = NotificationCompat.Builder(context, CHANNEL).setSmallIcon(R.drawable.ic_ceho_whirlpool)
            .setContentTitle("Трафик подписки заканчивается").setContentText(text).setStyle(NotificationCompat.BigTextStyle().bigText(text)).setAutoCancel(true)
            .setContentIntent(PendingIntent.getActivity(context, 5, Intent(context, MainActivity::class.java), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE))
            .build()
        runCatching { manager.notify(QUOTA_ID_BASE + (id and 0xff), note) }
    }

    private const val QUOTA_ID_BASE = 7000
}
