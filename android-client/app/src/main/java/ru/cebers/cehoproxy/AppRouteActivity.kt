package ru.cebers.cehoproxy

import android.app.AppOpsManager
import android.app.usage.UsageEvents
import android.app.usage.UsageStatsManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Bundle
import android.os.Process
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.core.graphics.drawable.toBitmap
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import ru.cebers.cehoproxy.core.AppRoutePolicy
import ru.cebers.cehoproxy.core.ProfileStore
import ru.cebers.cehoproxy.ui.CehoTheme
import ru.cebers.cehoproxy.ui.QuietButton
import ru.cebers.cehoproxy.ui.QuietDivider
import ru.cebers.cehoproxy.ui.QuietRow
import ru.cebers.cehoproxy.vpn.CehoVpnService

/** Opened from the VPN notification: switches the apps used a moment ago between the tunnel and a direct connection. */
class AppRouteActivity : ComponentActivity() {
    private var granted by mutableStateOf(false)
    private var recent by mutableStateOf<List<AppChoice>>(emptyList())
    private var routed by mutableStateOf<Set<String>>(emptySet())
    private var changed = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val theme = ThemePreference.fromStorage(getSharedPreferences("appearance", MODE_PRIVATE).getString("theme", null))
        setContent {
            CehoTheme(theme) {
                Box(Modifier.fillMaxSize().background(Color(0x66000000)).clickable(interactionSource = MutableInteractionSource(), indication = null) { finish() }, contentAlignment = Alignment.BottomCenter) {
                    Surface(
                        color = MaterialTheme.colorScheme.surface,
                        shape = RoundedCornerShape(topStart = 24.dp, topEnd = 24.dp),
                        modifier = Modifier.fillMaxWidth().clickable(interactionSource = MutableInteractionSource(), indication = null) {}.testTag("app-route-sheet"),
                    ) {
                        Column(Modifier.navigationBarsPadding().padding(20.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Text("Через VPN", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
                            if (!granted) {
                                Text(
                                    "Чтобы показать приложение, которое вы только что открыли, разрешите CehoProxy доступ к истории использования. В списке Android включите CehoProxy и вернитесь сюда.",
                                    style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 4.dp, bottom = 16.dp),
                                )
                                QuietButton("Разрешить доступ", ::openUsageAccess, Modifier.fillMaxWidth().testTag("app-route-grant"))
                            } else {
                                Text("Недавно открытые приложения. Изменения применятся сразу.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(bottom = 8.dp))
                                if (recent.isEmpty()) Text("Недавно открытых приложений нет.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(vertical = 12.dp))
                                recent.forEach { app ->
                                    val on = app.packageName in routed
                                    QuietRow(
                                        app.label,
                                        subtitle = if (on) "Через VPN" else "Напрямую",
                                        onClick = { toggle(app.packageName, !on) },
                                        leading = { Image(app.icon.toBitmap(96, 96).asImageBitmap(), contentDescription = null, modifier = Modifier.size(36.dp)) },
                                        trailing = { Switch(checked = on, onCheckedChange = { toggle(app.packageName, it) }, modifier = Modifier.testTag("app-route:${app.packageName}").semantics { contentDescription = "${app.label} через VPN" }) },
                                    )
                                    QuietDivider()
                                }
                            }
                            QuietButton("Готово", ::finish, Modifier.fillMaxWidth().padding(top = 12.dp).testTag("app-route-done"), primary = false)
                        }
                    }
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        granted = usageAccessGranted()
        if (granted) lifecycleScope.launch {
            recent = withContext(Dispatchers.IO) { recentApps() }
            val profile = ProfileStore(this@AppRouteActivity).read()
            routed = recent.filter { AppRoutePolicy.viaVpn(profile, it.packageName) }.map { it.packageName }.toSet()
        }
    }

    override fun onPause() {
        super.onPause()
        if (isFinishing && changed) restartTunnel()
    }

    private fun toggle(packageName: String, viaVpn: Boolean) {
        ProfileStore(this).update { AppRoutePolicy.setViaVpn(it, packageName, viaVpn) }
        routed = if (viaVpn) routed + packageName else routed - packageName
        changed = true
    }

    private fun restartTunnel() {
        if (!CehoVpnService.hasLiveServiceInstance()) return
        ContextCompat.startForegroundService(this, Intent(this, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_START))
    }

    private fun usageAccessGranted(): Boolean =
        getSystemService(AppOpsManager::class.java).unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), packageName) == AppOpsManager.MODE_ALLOWED

    private fun openUsageAccess() {
        val own = Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS, Uri.parse("package:$packageName"))
        val list = Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS)
        listOf(own, list).firstOrNull { it.resolveActivity(packageManager) != null }?.let { runCatching { startActivity(it) } }
    }

    private fun recentApps(): List<AppChoice> {
        val usage = getSystemService(Context.USAGE_STATS_SERVICE) as UsageStatsManager
        val now = System.currentTimeMillis()
        val events = usage.queryEvents(now - RECENT_WINDOW_MILLIS, now)
        val order = LinkedHashMap<String, Long>()
        val event = UsageEvents.Event()
        while (events.hasNextEvent()) {
            events.getNextEvent(event)
            if (event.eventType == UsageEvents.Event.ACTIVITY_RESUMED) order[event.packageName] = event.timeStamp
        }
        val home = packageManager.resolveActivity(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME), PackageManager.MATCH_DEFAULT_ONLY)?.activityInfo?.packageName
        val skip = setOfNotNull(packageName, home, "com.android.systemui", "com.android.settings")
        return order.entries.sortedByDescending { it.value }.map { it.key }.filterNot { it in skip }.mapNotNull { pkg ->
            if (packageManager.getLaunchIntentForPackage(pkg) == null) return@mapNotNull null
            runCatching {
                val info = packageManager.getApplicationInfo(pkg, 0)
                AppChoice(pkg, packageManager.getApplicationLabel(info).toString(), packageManager.getApplicationIcon(info))
            }.getOrNull()
        }.take(RECENT_LIMIT)
    }

    private companion object {
        const val RECENT_WINDOW_MILLIS = 60 * 60_000L
        const val RECENT_LIMIT = 5
    }
}
