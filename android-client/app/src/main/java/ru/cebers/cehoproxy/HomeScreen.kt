package ru.cebers.cehoproxy

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.LocalDpiPolicy
import ru.cebers.cehoproxy.core.NodeCountry
import ru.cebers.cehoproxy.core.NodePresentation
import ru.cebers.cehoproxy.core.ProxyNode
import ru.cebers.cehoproxy.core.SubscriptionMembership
import ru.cebers.cehoproxy.core.SubscriptionMetadata
import ru.cebers.cehoproxy.core.SubscriptionRefreshPolicy
import ru.cebers.cehoproxy.core.SubscriptionRemovalPolicy
import ru.cebers.cehoproxy.core.SubscriptionSummary
import ru.cebers.cehoproxy.core.SubscriptionSummary.TermKind
import ru.cebers.cehoproxy.core.SubscriptionSummary.TrafficKind
import ru.cebers.cehoproxy.core.TunnelAppsPolicy
import ru.cebers.cehoproxy.core.UptimeFormat
import ru.cebers.cehoproxy.core.VpnMeasurements
import ru.cebers.cehoproxy.ui.ChevronGlyph
import ru.cebers.cehoproxy.ui.cappedStyle
import ru.cebers.cehoproxy.ui.QuietBadge
import ru.cebers.cehoproxy.ui.QuietButton
import ru.cebers.cehoproxy.ui.QuietDivider
import ru.cebers.cehoproxy.ui.QuietRow
import ru.cebers.cehoproxy.ui.QuietTextButton
import ru.cebers.cehoproxy.ui.SlidersGlyph

@Composable private fun rememberClockMillis(stepMillis: Long): Long {
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(stepMillis) { while (true) { now = System.currentTimeMillis(); kotlinx.coroutines.delay(stepMillis) } }
    return now
}

@Composable internal fun HomeScreen(
    profile: ClientProfile,
    connected: Boolean,
    connectedSince: Long,
    serviceRunning: Boolean,
    blocked: Boolean,
    measurements: VpnMeasurements,
    notificationsBlocked: Boolean,
    notice: String?,
    appChoices: List<AppChoice>,
    onOpenNotificationSettings: () -> Unit,
    onConnect: () -> Unit,
    onStop: () -> Unit,
    onSelectNode: (Int) -> Unit,
    onOpenServers: () -> Unit,
    onOpenRules: () -> Unit,
    onOpenSubscription: (String) -> Unit,
    onAddSubscription: () -> Unit,
) {
    val colors = MaterialTheme.colorScheme
    val tunnelPackages = TunnelAppsPolicy.packages(profile, appChoices.map { it.packageName }).toSet()
    val tunnelApps = appChoices.filter { it.packageName in tunnelPackages }
    val starting = connected || notice == "VPN запускается"
    val directOnly = profile.nodes.isEmpty() && profile.importedConfig == null && LocalDpiPolicy.activeApps(profile).isNotEmpty()
    val needsSubscription = profile.nodes.isEmpty() && profile.importedConfig == null && !directOnly
    val node = profile.selectedNode ?: profile.nodes.firstOrNull()

    TunnelVortex(
        apps = tunnelApps, active = starting, connected = connected, running = serviceRunning, blocked = blocked,
        fullTunnel = profile.excludeSelectedApps, directOnly = directOnly,
        onClick = { if (serviceRunning) onStop() else onConnect() },
        modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
    )
    Text(
        when { blocked -> blockedTitle(notice); connected && directOnly -> "Обход DPI подключён"; connected -> "Подключено"; starting -> "Подключаем…"; else -> "Не подключено" },
        style = cappedStyle(MaterialTheme.typography.headlineLarge), color = colors.onSurface, modifier = Modifier.padding(top = 24.dp).testTag("home-status"),
    )
    Text(
        when {
            blocked -> if (profile.autoRotationEnabled) "Проверка повторится автоматически" else "Выберите другой сервер ниже или проверьте снова"
            connected -> "Нажмите на водоворот, чтобы выключить"
            starting -> "Нажмите на водоворот, чтобы отменить"
            needsSubscription -> "Сначала добавьте подписку"
            else -> "Нажмите на водоворот, чтобы подключить"
        },
        style = MaterialTheme.typography.bodyLarge, color = colors.onSurfaceVariant, modifier = Modifier.padding(top = 6.dp),
    )
    if (needsSubscription) QuietButton("Добавить подписку", onAddSubscription, Modifier.fillMaxWidth().padding(top = 20.dp).testTag("home-add-node"))
    if (blocked) QuietButton("Проверить серверы снова", onConnect, Modifier.fillMaxWidth().padding(top = 16.dp).testTag("home-retry"), primary = false)
    val fastestIndex = profile.nodes.withIndex().filter { nodeAllowed(profile, it.value) && measurements.nodes[it.value.routeId]?.latencyMs != null && measurements.nodes[it.value.routeId]?.failed != true }
        .minByOrNull { measurements.nodes[it.value.routeId]?.latencyMs ?: Int.MAX_VALUE }?.index
    if (blocked && !profile.autoRotationEnabled && fastestIndex != null) QuietButton("Подключиться к самому быстрому", { onSelectNode(fastestIndex); onConnect() }, Modifier.fillMaxWidth().padding(top = 8.dp).testTag("home-switch-fastest"), primary = false)
    if (notificationsBlocked) Surface(color = colors.errorContainer, shape = MaterialTheme.shapes.large, modifier = Modifier.fillMaxWidth().padding(top = 16.dp).testTag("home-notifications-off")) {
        Column(Modifier.padding(16.dp)) {
            Text("Уведомления выключены", color = colors.onErrorContainer, style = MaterialTheme.typography.titleSmall)
            Text("Вы не увидите, что VPN перестал работать. Включите уведомления для CehoProxy.", color = colors.onErrorContainer, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 4.dp))
            QuietTextButton(onClick = onOpenNotificationSettings, modifier = Modifier.heightIn(min = 48.dp)) { Text("Включить") }
        }
    }

    QuietDivider(Modifier.padding(top = 24.dp))
    ServerSummaryRow(profile, node, measurements, directOnly, onOpenServers)
    QuietDivider()

    val url = node?.let { SubscriptionRemovalPolicy.urlFor(profile, it.subscriptionMetadata) }
    val expires = node?.let { it.subscriptionExpiresAt ?: it.subscriptionMetadata.expiresAtEpochSeconds }
    val hasSubscriptionFacts = node != null && (url != null || expires != null || node.subscriptionMetadata.usedBytes != null || node.subscriptionMetadata.totalBytes != null)
    val clock = rememberClockMillis(if (connected) 15_000L else 60_000L)
    Row(Modifier.fillMaxWidth().padding(top = 16.dp), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
        SummaryValue("В сети", if (connected && connectedSince > 0) UptimeFormat.words(clock - connectedSince) else "—", Modifier.weight(1f).testTag("home-uptime"), muted = !connected)
        if (hasSubscriptionFacts && node != null) {
            val term = SubscriptionSummary.term(expires, clock / 1000)
            SummaryValue(
                "До окончания подписки", term.label, Modifier.weight(1f).testTag("home-term"),
                muted = term.kind == TermKind.UNKNOWN,
                tone = when (term.kind) { TermKind.EXPIRED -> colors.error; TermKind.LAST_DAY -> colors.tertiary; else -> colors.onSurface },
                onClick = url?.let { u -> { onOpenSubscription(u) } },
            )
        }
    }
    if (hasSubscriptionFacts && node != null) SubscriptionSummaryBlock(profile, node, url, onOpenSubscription)
    QuietDivider(Modifier.padding(top = 16.dp))
    QuietRow(
        "Правила подключения", Modifier.testTag("home-rules"), subtitle = "Приложения и сайты", onClick = onOpenRules,
        leading = { QuietBadge { SlidersGlyph(colors.primary) } },
    )
    if (notice != null && notice != "VPN запускается") Text(
        notice, color = if (notice.contains("ошиб", true) || notice.contains("не выдано", true)) colors.error else colors.primary,
        style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 12.dp).testTag("home-notice"),
    )
}

@Composable private fun ServerSummaryRow(profile: ClientProfile, node: ProxyNode?, measurements: VpnMeasurements, directOnly: Boolean, onOpenServers: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    when {
        node != null -> {
            val measurement = measurements.nodes[node.routeId]
            val code = NodeCountry.resolve(node.name) ?: measurement?.verifiedCountry?.countryCode
            val country = code?.let { NodeCountry.displayName(it) }
            val name = NodePresentation.withoutFlag(NodePresentation.routeName(node)).let { plain -> if (country != null) plain.removePrefix(country).trim().ifBlank { plain } else plain }
            val ping = measurement?.latencyMs?.let { "$it мс" }
            QuietRow(
                title = country ?: name,
                subtitle = listOfNotNull(name.takeIf { code != null }, node.type.uppercase().takeIf { code == null }, ping).joinToString(" · "),
                onClick = onOpenServers,
                leading = { QuietBadge { NodeCountryMark(measurement?.verifiedCountry, NodeCountry.resolve(node.name)) } },
                modifier = Modifier.testTag("home-server"),
            )
        }
        directOnly -> QuietRow("Локальный обход DPI", subtitle = "Без удалённого сервера", leading = { QuietBadge { ShieldBadgeGlyph(colors.primary) } }, modifier = Modifier.testTag("home-server"))
        profile.importedConfig != null -> QuietRow("Импортированная конфигурация", subtitle = "Серверы заданы файлом настроек", leading = { QuietBadge { ShieldBadgeGlyph(colors.primary) } }, modifier = Modifier.testTag("home-server"))
        else -> QuietRow("Сервер не выбран", subtitle = "Добавьте подписку, чтобы появились серверы", leading = { QuietBadge { ShieldBadgeGlyph(colors.outline) } }, modifier = Modifier.testTag("home-server"))
    }
}

@Composable private fun ShieldBadgeGlyph(color: Color) = ru.cebers.cehoproxy.ui.ShieldGlyph(color)

@Composable private fun SummaryValue(label: String, value: String, modifier: Modifier = Modifier, muted: Boolean = false, tone: Color = MaterialTheme.colorScheme.onSurface, onClick: (() -> Unit)? = null) {
    Column(modifier.let { if (onClick != null) it.clickable(role = Role.Button, onClick = onClick) else it }.heightIn(min = 48.dp)) {
        Text(label, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 6.dp)) {
            Text(value, style = MaterialTheme.typography.headlineSmall, color = if (muted) MaterialTheme.colorScheme.onSurfaceVariant else tone, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
            if (onClick != null) { Spacer(Modifier.width(2.dp)); ChevronGlyph(MaterialTheme.colorScheme.onSurfaceVariant) }
        }
    }
}

@Composable private fun SubscriptionSummaryBlock(profile: ClientProfile, node: ProxyNode, url: String?, onOpenSubscription: (String) -> Unit) {
    val colors = MaterialTheme.colorScheme
    val metadata: SubscriptionMetadata = node.subscriptionMetadata
    val traffic = SubscriptionSummary.traffic(metadata.usedBytes, metadata.totalBytes)
    val title = metadata.title ?: metadata.sourceHost ?: "Подписка"
    val updated = url?.let { profile.subscriptionFetchedAtByUrl[it] }?.let { SubscriptionRefreshPolicy.fetchedLabel(it, System.currentTimeMillis()) }
    val serverCount = url?.let { u -> profile.nodes.count { SubscriptionMembership.belongs(it, u, profile.subscriptionUrls) } }
    Column(Modifier.fillMaxWidth().padding(top = 20.dp).testTag("home-subscription")) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(title, style = MaterialTheme.typography.titleMedium, color = colors.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                val meta = listOfNotNull(serverCount?.let { "$it ${SubscriptionSummary.plural(it.toLong(), "сервер", "сервера", "серверов")}" }, updated).joinToString(" · ")
                if (meta.isNotEmpty()) Text(meta, style = MaterialTheme.typography.bodySmall, color = colors.onSurfaceVariant)
            }
            if (url != null) QuietTextButton(onClick = { onOpenSubscription(url) }, modifier = Modifier.heightIn(min = 48.dp).testTag("home-subscription-details")) {
                Text("Подробнее", color = colors.onSurfaceVariant)
                Spacer(Modifier.width(2.dp))
                ChevronGlyph(colors.onSurfaceVariant)
            }
        }
        if (traffic.kind != TrafficKind.NONE) {
            if (traffic.fraction != null) Canvas(Modifier.fillMaxWidth().padding(top = 10.dp).height(6.dp)) {
                drawRoundRect(colors.outlineVariant, cornerRadius = CornerRadius(size.height / 2))
                drawRoundRect(if (traffic.kind == TrafficKind.OVER_LIMIT) colors.error else colors.primary, size = Size(size.width * traffic.fraction, size.height), cornerRadius = CornerRadius(size.height / 2))
            }
            val (left, right) = when (traffic.kind) {
                TrafficKind.LIMITED -> "${SubscriptionSummary.bytes(traffic.used ?: 0)} из ${SubscriptionSummary.bytes(traffic.total ?: 0)} использовано" to "Осталось ${SubscriptionSummary.bytes(traffic.remaining ?: 0)}"
                TrafficKind.OVER_LIMIT -> "${SubscriptionSummary.bytes(traffic.used ?: 0)} из ${SubscriptionSummary.bytes(traffic.total ?: 0)} использовано" to "Лимит превышен"
                TrafficKind.UNKNOWN_LIMIT -> "Использовано ${SubscriptionSummary.bytes(traffic.used ?: 0)}" to "Лимит не указан"
                TrafficKind.UNLIMITED -> (traffic.used?.let { "Использовано ${SubscriptionSummary.bytes(it)}" } ?: "Трафик") to "Без лимита"
                TrafficKind.LIMIT_ONLY -> "Лимит ${SubscriptionSummary.bytes(traffic.total ?: 0)}" to "Расход не указан"
                TrafficKind.NONE -> "" to ""
            }
            Row(Modifier.fillMaxWidth().padding(top = 8.dp).testTag("home-traffic"), horizontalArrangement = Arrangement.SpaceBetween) {
                Text(left, style = MaterialTheme.typography.bodySmall, color = colors.onSurfaceVariant, modifier = Modifier.weight(1f, fill = false))
                Spacer(Modifier.width(12.dp))
                Text(right, style = MaterialTheme.typography.bodySmall, color = if (traffic.kind == TrafficKind.OVER_LIMIT) colors.error else colors.onSurfaceVariant)
            }
        }
    }
}
