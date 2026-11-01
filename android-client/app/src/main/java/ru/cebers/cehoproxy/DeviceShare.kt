package ru.cebers.cehoproxy

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.background
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.SubscriptionShare
import ru.cebers.cehoproxy.core.SubscriptionSummary
import ru.cebers.cehoproxy.ui.QuietButton
import ru.cebers.cehoproxy.ui.QuietDivider
import ru.cebers.cehoproxy.ui.QuietRow
import ru.cebers.cehoproxy.ui.QuietTextButton

internal data class SendRequest(val keys: Set<String>, val target: String?)

private fun servers(count: Int) = "$count ${SubscriptionSummary.plural(count.toLong(), "сервер", "сервера", "серверов")}"

/** Asks the sender which subscriptions to send; when the target is unknown, lists devices found in the network. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable internal fun SendToDeviceSheet(profile: ClientProfile, request: SendRequest, onScanQr: () -> Unit, onSend: (Set<String>, String) -> Unit, onDismiss: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    val groups = remember(profile.nodes, profile.subscriptionUrls) { SubscriptionShare.groups(profile) }
    var chosen by remember { mutableStateOf(request.keys.ifEmpty { groups.map { it.key }.toSet() }) }
    var picked by remember { mutableStateOf<String?>(null) }
    var devices by remember { mutableStateOf<List<ShareDiscovery.Device>>(emptyList()) }
    val context = LocalContext.current
    if (request.target == null) DisposableEffect(Unit) {
        val discovery = ShareDiscovery(context) { devices = it }
        discovery.start()
        onDispose { discovery.stop() }
    }
    val target = request.target ?: picked?.takeIf { url -> devices.any { it.url == url } }
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = colors.surfaceContainer) {
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp).padding(bottom = 28.dp).testTag("send-device-sheet")) {
            Text("Отправить на устройство", style = MaterialTheme.typography.titleLarge, color = colors.onSurface)
            Text("Телевизор, магнитола или другой телефон получат выбранные подписки. Свои подписки на устройстве сохранятся.", style = MaterialTheme.typography.bodyMedium, color = colors.onSurfaceVariant, modifier = Modifier.padding(top = 4.dp, bottom = 12.dp))
            Text("Что отправить", style = MaterialTheme.typography.titleSmall, color = colors.onSurface, modifier = Modifier.padding(top = 8.dp, bottom = 4.dp))
            if (groups.isEmpty()) Text("На этом телефоне нет подписок.", color = colors.onSurfaceVariant)
            groups.forEach { group ->
                val on = group.key in chosen
                QuietRow(
                    group.title, subtitle = servers(group.nodes.size),
                    onClick = { chosen = if (on) chosen - group.key else chosen + group.key },
                    trailing = { Checkbox(checked = on, onCheckedChange = { chosen = if (it) chosen + group.key else chosen - group.key }, modifier = Modifier.testTag("send-choose:${group.title}")) },
                )
                QuietDivider()
            }
            Text("Куда", style = MaterialTheme.typography.titleSmall, color = colors.onSurface, modifier = Modifier.padding(top = 16.dp, bottom = 4.dp))
            if (request.target != null) {
                QuietRow("Устройство из QR-кода", subtitle = runCatching { java.net.URI(request.target).host }.getOrDefault(request.target), selected = true)
            } else {
                devices.forEach { device ->
                    QuietRow(
                        device.name, subtitle = "В этой сети", selected = picked == device.url, onClick = { picked = device.url },
                        trailing = { RadioButton(selected = picked == device.url, onClick = { picked = device.url }, modifier = Modifier.testTag("send-device:${device.name}")) },
                    )
                    QuietDivider()
                }
                Row(Modifier.fillMaxWidth().padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp)
                    Text(if (devices.isEmpty()) "Ищем устройства…" else "Ищем ещё…", style = MaterialTheme.typography.bodyMedium, color = colors.onSurfaceVariant)
                }
                Text(
                    "На устройстве откройте CehoProxy и нажмите «Получить с телефона». Телефон и устройство должны быть в одной сети Wi-Fi; для магнитолы подойдёт точка доступа этого телефона.",
                    style = MaterialTheme.typography.bodySmall, color = colors.onSurfaceVariant,
                )
                QuietTextButton(onClick = onScanQr, modifier = Modifier.padding(top = 4.dp).testTag("send-scan-qr")) { Text("Устройства нет в списке — сканировать QR-код") }
            }
            QuietButton(
                "Отправить", { target?.let { onSend(chosen, it) } },
                Modifier.fillMaxWidth().padding(top = 16.dp).testTag("send-device-submit"),
                enabled = chosen.isNotEmpty() && target != null && groups.isNotEmpty(),
            )
        }
    }
}

@Composable internal fun ReceiveFromPhoneDialog(url: String, deviceName: String, onCancel: () -> Unit) {
    AlertDialog(
        onDismissRequest = onCancel,
        title = { Text("Получить с телефона") },
        text = {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("На телефоне откройте CehoProxy → «Подписки» → «Отправить на устройство» и выберите «$deviceName». Или наведите камеру телефона на QR-код. Телефон и это устройство должны быть в одной сети Wi-Fi.")
                QrCodeImage(url, Modifier.padding(top = 16.dp).size(200.dp).background(Color.White).padding(8.dp).testTag("receive-qr"))
                Text(url, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp))
            }
        },
        confirmButton = { QuietTextButton(onClick = onCancel, modifier = Modifier.testTag("receive-cancel")) { Text("Отмена") } },
    )
}

@Composable internal fun IncomingShareDialog(summary: List<Pair<String, Int>>, onAccept: () -> Unit, onDecline: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDecline,
        title = { Text("Добавить подписки с телефона?") },
        text = { Column { summary.forEach { (title, count) -> Text("$title — ${servers(count)}", modifier = Modifier.padding(vertical = 2.dp)) } } },
        confirmButton = { QuietTextButton(onClick = onAccept, modifier = Modifier.testTag("incoming-accept")) { Text("Добавить") } },
        dismissButton = { QuietTextButton(onClick = onDecline) { Text("Отклонить") } },
    )
}

@Composable internal fun IncomingLinkDialog(link: String, onAccept: () -> Unit, onDecline: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDecline,
        title = { Text("Добавить ссылку с телефона?") },
        text = { Text(runCatching { java.net.URI(link).host }.getOrNull()?.let { "Подписка: $it" } ?: link.take(120)) },
        confirmButton = { QuietTextButton(onClick = onAccept, modifier = Modifier.testTag("incoming-link-accept")) { Text("Добавить") } },
        dismissButton = { QuietTextButton(onClick = onDecline) { Text("Отклонить") } },
    )
}
