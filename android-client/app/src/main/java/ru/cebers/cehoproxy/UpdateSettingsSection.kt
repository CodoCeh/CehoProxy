package ru.cebers.cehoproxy

import ru.cebers.cehoproxy.ui.QuietTextButton
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import java.text.DateFormat
import java.util.Date

@Composable
internal fun ObserveAutomaticUpdates() {
    val context = LocalContext.current
    val controller = remember { AndroidUpdateController.get(context) }
    val state by controller.state.collectAsState()
    val preferences = remember { context.getSharedPreferences("android_updates", android.content.Context.MODE_PRIVATE) }
    var dismissed by remember { mutableStateOf(preferences.getString("dismissed_version", null)) }
    LaunchedEffect(Unit) { controller.checkIfDue() }
    val candidate = state.candidate
    val key = candidate?.versionName ?: candidate?.name
    if (candidate != null && key != null && key != dismissed) {
        AlertDialog(
            onDismissRequest = { if (!state.busy) { preferences.edit().putString("dismissed_version", key).apply(); dismissed = key } },
            title = { Text("Доступно обновление") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Вышла новая версия CehoProxy${candidate.versionName?.let { ": $it" } ?: ""}. Сейчас установлена ${BuildConfig.VERSION_NAME}.")
                    Text(if (state.ready != null) "Файл загружен и проверен. Осталось подтвердить установку в системном окне Android." else "Файл скачается с GitHub, перед установкой проверим версию и подпись. Настройки сохранятся.", style = MaterialTheme.typography.bodySmall)
                    if (state.busy) state.progress?.let { LinearProgressIndicator(progress = { it / 100f }, modifier = Modifier.fillMaxWidth()); Text("Загружено $it%") } ?: LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
                    if (!state.busy && state.ready == null && state.message.isNotBlank() && !state.message.startsWith("Android APK найден")) Text(state.message, style = MaterialTheme.typography.bodySmall)
                }
            },
            confirmButton = {
                if (state.ready != null) QuietTextButton(onClick = { controller.install(context) }, modifier = Modifier.testTag("update-dialog-install")) { Text("Установить") }
                else QuietTextButton(onClick = controller::download, modifier = Modifier.testTag("update-dialog-download")) { Text(if (state.busy) "Загрузка…" else "Обновить") }
            },
            dismissButton = {
                QuietTextButton(onClick = { preferences.edit().putString("dismissed_version", key).apply(); dismissed = key }, modifier = Modifier.testTag("update-dialog-later")) { Text("Позже") }
            },
        )
    }
}

@Composable
internal fun UpdateSettingsSection() {
    val context = LocalContext.current
    val controller = remember { AndroidUpdateController.get(context) }
    val state by controller.state.collectAsState()
    LaunchedEffect(Unit) { controller.checkIfDue() }
    Column(verticalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.fillMaxWidth().testTag("settings-update-section")) {
        Text("Обновления приложения", style = MaterialTheme.typography.titleMedium)
        Text("Установлена версия ${BuildConfig.VERSION_NAME}. Источник — CodoCeh/CehoProxy на GitHub.", style = MaterialTheme.typography.bodySmall)
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text("Проверять автоматически", modifier = Modifier.weight(1f))
            Switch(checked = state.automatic, onCheckedChange = controller::automatic, modifier = Modifier.semantics { contentDescription = "Проверять обновления автоматически" })
        }
        Text("Проверка при открытии приложения, не чаще раза в сутки. Установка — только после вашего подтверждения.", style = MaterialTheme.typography.bodySmall)
        Text(state.message)
        if (state.lastChecked > 0) Text("Последняя успешная проверка: ${DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT).format(Date(state.lastChecked))}", style = MaterialTheme.typography.bodySmall)
        if (state.busy) {
            state.progress?.let { LinearProgressIndicator(progress = { it / 100f }, modifier = Modifier.fillMaxWidth()); Text("Загружено $it%") }
                ?: LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = controller::check, enabled = !state.busy, modifier = Modifier.testTag("settings-update-check")) { Text("Проверить обновления") }
            if (state.ready != null) Button(onClick = { controller.install(context) }, enabled = !state.busy) { Text("Установить") }
            else if (state.candidate != null) Button(onClick = controller::download, enabled = !state.busy) { Text("Обновить") }
        }
    }
}
