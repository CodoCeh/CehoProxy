package ru.cebers.cehoproxy

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.PowerManager
import android.provider.Settings
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import ru.cebers.cehoproxy.ui.BatteryGlyph
import ru.cebers.cehoproxy.ui.QuietBadge
import ru.cebers.cehoproxy.ui.QuietDivider
import ru.cebers.cehoproxy.ui.QuietRow

internal object BatteryExemption {
    fun granted(context: Context) =
        context.getSystemService(PowerManager::class.java)?.isIgnoringBatteryOptimizations(context.packageName) == true

    fun open(context: Context) {
        val pm = context.packageManager
        val ask = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:${context.packageName}"))
        val list = Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)
        val details = Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:${context.packageName}"))
        val intent = listOf(if (granted(context)) list else ask, list, details).firstOrNull { it.resolveActivity(pm) != null } ?: return
        runCatching { context.startActivity(intent) }
    }
}

@Composable
internal fun BatteryExemptionRow() {
    val context = LocalContext.current
    if (context.packageManager.hasSystemFeature(PackageManager.FEATURE_LEANBACK)) return
    val lifecycleState by androidx.lifecycle.compose.LocalLifecycleOwner.current.lifecycle.currentStateFlow.collectAsState()
    val granted = remember(lifecycleState) { BatteryExemption.granted(context) }
    QuietRow(
        "Не выгружать из памяти",
        subtitle = if (granted) "Android не будет останавливать CehoProxy для экономии батареи" else "Android может закрыть приложение в фоне, чтобы сберечь батарею",
        onClick = { BatteryExemption.open(context) },
        leading = { QuietBadge { BatteryGlyph(MaterialTheme.colorScheme.primary) } },
        trailing = { Switch(checked = granted, onCheckedChange = { BatteryExemption.open(context) }, modifier = Modifier.testTag("settings-battery-switch").semantics { contentDescription = "Не выгружать из памяти" }) },
        modifier = Modifier.testTag("settings-battery"),
    )
    QuietDivider()
}
