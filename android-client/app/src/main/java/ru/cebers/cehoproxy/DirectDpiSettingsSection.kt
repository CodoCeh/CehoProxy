package ru.cebers.cehoproxy

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import ru.cebers.cehoproxy.core.*

@Composable
internal fun DirectDpiSettingsSection(profile: ClientProfile, apps: List<AppChoice>, onChange: (ClientProfile) -> Unit) {
    var choosing by remember { mutableStateOf(false) }
    val enabled = profile.importedConfig == null
    val included = if (profile.excludeSelectedApps) apps.filter { it.packageName !in profile.apps } else apps.filter { it.packageName in profile.apps }
    fun setMode(packageName: String, direct: Boolean) {
        onChange(DirectDpiUiPolicy.setMode(profile, packageName, direct))
    }
    Column(Modifier.fillMaxWidth().padding(top = 16.dp).testTag("dpi-settings-section"), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("Обход DPI без смены IP", style = MaterialTheme.typography.titleMedium)
        Text("Экспериментально: разделяем записи TLS и блокируем QUIC для выбранного приложения. Оно выходит напрямую с вашим IP. Работает не у всех операторов и не убирает рекламу.", style = MaterialTheme.typography.bodySmall)
        if (!enabled) Text("Обход DPI CehoProxy не применяется к импортированной конфигурации. Её маршруты сохраняются.", style = MaterialTheme.typography.bodySmall)
        if (profile.nodes.isEmpty() && enabled) Text("Без VPN-узла работают приложения с обходом DPI; для остальных выбранных приложений добавьте подписку.", style = MaterialTheme.typography.bodySmall)
        profile.localDpiApps.forEach { packageName ->
            Row(Modifier.fillMaxWidth(), verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
                Text(apps.firstOrNull { it.packageName == packageName }?.label ?: packageName, modifier = Modifier.weight(1f))
                TextButton(onClick = { setMode(packageName, false) }, enabled = enabled, modifier = Modifier.testTag("dpi-disable:$packageName")) { Text("Через VPN-узел") }
            }
        }
        OutlinedButton(onClick = { choosing = true }, enabled = enabled, modifier = Modifier.testTag("dpi-app-picker")) { Text("Выбрать приложение") }
        val youtube = apps.firstOrNull { it.packageName == "com.google.android.youtube" }
        if (youtube != null && youtube.packageName !in profile.localDpiApps) TextButton(onClick = { setMode(youtube.packageName, true) }, enabled = enabled, modifier = Modifier.testTag("dpi-add-youtube")) { Text("Добавить YouTube") }
    }
    if (choosing) AlertDialog(onDismissRequest = { choosing = false }, title = { Text("Обход DPI для приложения") }, text = {
        Column(Modifier.heightIn(max = 360.dp).verticalScroll(rememberScrollState())) {
            Text("Выберите приложение в вашем туннеле. Его IP останется прежним.", style = MaterialTheme.typography.bodySmall)
            included.filterNot { it.packageName in profile.localDpiApps }.forEach { app ->
                TextButton(onClick = { setMode(app.packageName, true); choosing = false }, modifier = Modifier.testTag("dpi-enable:${app.packageName}")) { Text(app.label) }
            }
            if (included.isEmpty()) Text("Сначала выберите приложения для туннеля.")
        }
    }, confirmButton = { TextButton(onClick = { choosing = false }) { Text("Закрыть") } })
}
