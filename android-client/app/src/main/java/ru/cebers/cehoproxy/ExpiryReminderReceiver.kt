package ru.cebers.cehoproxy

import android.app.AlarmManager
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.ExpiryReminderPolicy
import ru.cebers.cehoproxy.core.ProfileStore
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object ExpiryReminderScheduler {
    private const val PREFS = "expiry_reminders"
    private const val KEY_IDS = "scheduled_ids"
    private const val CHANNEL = "subscription_expiry"
    private const val EXTRA_EXPIRY = "expiry"

    fun reconcile(context: Context, profile: ClientProfile, nowEpochSeconds: Long = System.currentTimeMillis() / 1000) {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val manager = context.getSystemService(AlarmManager::class.java)
        prefs.getStringSet(KEY_IDS, emptySet()).orEmpty().forEach { id ->
            val pending = pendingIntent(context, id.toInt(), null, PendingIntent.FLAG_NO_CREATE)
            if (pending != null) { manager.cancel(pending); pending.cancel() }
        }
        val planned = if (profile.expiryRemindersEnabled) profile.nodes.mapNotNull { it.subscriptionExpiresAt }
            .distinct().flatMap { expiry ->
                ExpiryReminderPolicy.reminderTimes(expiry, nowEpochSeconds).map { time ->
                    val id = ExpiryReminderPolicy.notificationId(expiry, time)
                    val pending = pendingIntent(context, id, expiry, PendingIntent.FLAG_UPDATE_CURRENT)!!
                    manager.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, time * 1000, pending)
                    id.toString()
                }
            }.toSet() else emptySet()
        prefs.edit().putStringSet(KEY_IDS, planned).apply()
    }

    private fun pendingIntent(context: Context, id: Int, expiry: Long?, mode: Int): PendingIntent? {
        val intent = Intent(context, ReminderReceiver::class.java).putExtra(EXTRA_EXPIRY, expiry ?: 0L)
        return PendingIntent.getBroadcast(context, id, intent, mode or PendingIntent.FLAG_IMMUTABLE)
    }

    class BootReceiver : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent?) {
            if (intent?.action == Intent.ACTION_BOOT_COMPLETED) ExpiryReminderScheduler.reconcile(context, ProfileStore(context).read())
        }
    }

    class ReminderReceiver : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent?) {
            val expiry = intent?.getLongExtra(EXTRA_EXPIRY, 0L)?.takeIf { it > 0 } ?: return
            val manager = context.getSystemService(NotificationManager::class.java)
            if (Build.VERSION.SDK_INT >= 26) manager.createNotificationChannel(NotificationChannel(CHANNEL, "Срок подписки", NotificationManager.IMPORTANCE_DEFAULT))
            val date = SimpleDateFormat("dd.MM.yyyy", Locale.getDefault()).format(Date(expiry * 1000))
            val contentIntent = PendingIntent.getActivity(context, 0, Intent(context, MainActivity::class.java), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
            val notification = NotificationCompat.Builder(context, CHANNEL)
                .setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle("Скоро закончится подписка")
                .setContentText("Подписка действует до $date. Не забудьте продлить её.")
                .setStyle(NotificationCompat.BigTextStyle().bigText("Подписка действует до $date. Не забудьте продлить её."))
                .setContentIntent(contentIntent).setAutoCancel(true).build()
            manager.notify(ExpiryReminderPolicy.notificationId(expiry, expiry), notification)
        }
    }
}
