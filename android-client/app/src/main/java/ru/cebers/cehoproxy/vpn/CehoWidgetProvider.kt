package ru.cebers.cehoproxy.vpn

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.widget.RemoteViews
import ru.cebers.cehoproxy.R

class CehoWidgetProvider : AppWidgetProvider() {
    override fun onUpdate(context: Context, manager: AppWidgetManager, appWidgetIds: IntArray) {
        appWidgetIds.forEach { manager.updateAppWidget(it, views(context)) }
    }

    companion object {
        fun refreshAll(context: Context) {
            val manager = AppWidgetManager.getInstance(context)
            val ids = manager.getAppWidgetIds(ComponentName(context, CehoWidgetProvider::class.java))
            ids.forEach { manager.updateAppWidget(it, views(context)) }
        }

        private fun views(context: Context): RemoteViews {
            val status = context.getSharedPreferences("vpn_status", Context.MODE_PRIVATE).getString("status", "stopped")
            val text = when (status) {
                "connected" -> "Включено"
                "starting" -> "Подключаем…"
                "error" -> "Ошибка"
                else -> "Выключено"
            }
            val tap = PendingIntent.getActivity(
                context, 0, Intent(context, ToggleVpnActivity::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            return RemoteViews(context.packageName, R.layout.ceho_widget).apply {
                setTextViewText(R.id.widget_status, "CehoProxy: $text")
                setOnClickPendingIntent(R.id.widget_root, tap)
            }
        }
    }
}
