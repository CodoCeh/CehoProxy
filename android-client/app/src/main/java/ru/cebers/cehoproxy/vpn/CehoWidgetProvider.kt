package ru.cebers.cehoproxy.vpn

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.view.View
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
            val prefs = context.getSharedPreferences("vpn_status", Context.MODE_PRIVATE)
            val status = prefs.getString("status", "stopped")
            val text = when (status) {
                "connected" -> "VPN включён"
                "starting" -> "Подключаем…"
                "error" -> "VPN: ошибка"
                else -> "VPN выключен"
            }
            val node = prefs.getString("widget_node", null).takeIf { status == "connected" || status == "starting" }
            val speed = prefs.getString("widget_speed", null).takeIf { status == "connected" }
            val tap = PendingIntent.getActivity(
                context, 0, Intent(context, ToggleVpnActivity::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            val next = PendingIntent.getActivity(
                context, 1, Intent(context, NextServerActivity::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
            )
            return RemoteViews(context.packageName, R.layout.ceho_widget).apply {
                setTextViewText(R.id.widget_status, text)
                setTextViewText(R.id.widget_node, node.orEmpty())
                setViewVisibility(R.id.widget_node, if (node.isNullOrBlank()) View.GONE else View.VISIBLE)
                setTextViewText(R.id.widget_speed, speed.orEmpty())
                setViewVisibility(R.id.widget_speed, if (speed.isNullOrBlank()) View.GONE else View.VISIBLE)
                setOnClickPendingIntent(R.id.widget_info, tap)
                setOnClickPendingIntent(R.id.widget_next, next)
            }
        }
    }
}
