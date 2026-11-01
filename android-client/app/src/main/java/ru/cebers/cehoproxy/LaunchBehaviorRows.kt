package ru.cebers.cehoproxy

import android.content.Context
import android.content.pm.PackageManager
import androidx.compose.material3.Switch
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.material3.MaterialTheme
import ru.cebers.cehoproxy.ui.MinimizeGlyph
import ru.cebers.cehoproxy.ui.PowerGlyph
import ru.cebers.cehoproxy.ui.QuietBadge
import ru.cebers.cehoproxy.ui.QuietDivider
import ru.cebers.cehoproxy.ui.QuietRow

internal object LaunchBehavior {
    private const val PREFS = "launch_behavior"
    const val AUTO_CONNECT = "auto_connect"
    const val MINIMIZE = "minimize_after_connect"

    fun enabled(context: Context, key: String) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(key, context.packageManager.hasSystemFeature(PackageManager.FEATURE_LEANBACK))

    fun set(context: Context, key: String, value: Boolean) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(key, value).apply()
}

@Composable
internal fun LaunchBehaviorRows() {
    val context = LocalContext.current
    var autoConnect by remember { mutableStateOf(LaunchBehavior.enabled(context, LaunchBehavior.AUTO_CONNECT)) }
    var minimize by remember { mutableStateOf(LaunchBehavior.enabled(context, LaunchBehavior.MINIMIZE)) }
    QuietRow(
        "Подключаться при запуске",
        subtitle = "VPN включится сам, как только откроется приложение",
        leading = { QuietBadge { PowerGlyph(MaterialTheme.colorScheme.primary) } },
        trailing = { Switch(checked = autoConnect, onCheckedChange = { autoConnect = it; LaunchBehavior.set(context, LaunchBehavior.AUTO_CONNECT, it) }, modifier = Modifier.testTag("settings-auto-connect").semantics { contentDescription = "Подключаться при запуске" }) },
    )
    QuietDivider()
    QuietRow(
        "Сворачивать после подключения",
        subtitle = "Приложение уйдёт в фон, когда VPN подключится",
        leading = { QuietBadge { MinimizeGlyph(MaterialTheme.colorScheme.primary) } },
        trailing = { Switch(checked = minimize, onCheckedChange = { minimize = it; LaunchBehavior.set(context, LaunchBehavior.MINIMIZE, it) }, modifier = Modifier.testTag("settings-minimize").semantics { contentDescription = "Сворачивать после подключения" }) },
    )
    QuietDivider()
}
