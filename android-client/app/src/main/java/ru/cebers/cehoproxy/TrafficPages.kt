package ru.cebers.cehoproxy

import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.*
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.core.graphics.drawable.toBitmap
import androidx.compose.ui.graphics.asImageBitmap
import kotlinx.coroutines.delay
import ru.cebers.cehoproxy.core.*
import ru.cebers.cehoproxy.ui.*
import ru.cebers.cehoproxy.vpn.TrafficMonitor

private fun exitLabels(profile: ClientProfile): (String) -> String {
    val byTag = RoutingConfig.tagsByRaw(profile.nodes, profile.excludedCountrySets, profile.disabledSubscriptions.toSet())
        .entries.mapNotNull { (raw, tag) -> profile.nodes.firstOrNull { it.raw == raw }?.let { tag to NodePresentation.routeName(it) } }.toMap()
    return { tag -> byTag[tag] ?: "сервер" }
}

/** Launcher apps come with the list; system services such as Google Play services are looked up by package. */
@Composable private fun rememberApps(appChoices: List<AppChoice>): (String) -> AppChoice? {
    val packageManager = LocalContext.current.packageManager
    val cache = remember { HashMap<String, AppChoice?>() }
    return { packageName ->
        cache.getOrPut(packageName) {
            appChoices.firstOrNull { it.packageName == packageName } ?: runCatching {
                val info = packageManager.getApplicationInfo(packageName, 0)
                AppChoice(packageName, packageManager.getApplicationLabel(info).toString(), packageManager.getApplicationIcon(info))
            }.getOrNull()
        }
    }
}

@Composable private fun AppIcon(app: AppChoice?) {
    val bitmap = remember(app?.packageName) { runCatching { app?.icon?.toBitmap(96, 96)?.asImageBitmap() }.getOrNull() }
    if (bitmap != null) Image(bitmap, contentDescription = null, modifier = Modifier.size(40.dp)) else Spacer(Modifier.size(40.dp))
}

@Composable internal fun TrafficRoutesPage(profile: ClientProfile, connected: Boolean, appChoices: List<AppChoice>, onSave: (ClientProfile) -> Unit) {
    var samples by remember { mutableStateOf(TrafficMonitor.latest) }
    LaunchedEffect(Unit) { while (true) { samples = TrafficMonitor.latest; delay(2_000) } }
    val label = remember(profile) { exitLabels(profile) }
    val rows = remember(samples, label) { TrafficRows.rows(samples.filter { it.packageName != TrafficRows.UNKNOWN_APP }, label) }
    val app = rememberApps(appChoices)
    PageHeading("Куда идёт трафик", subtitle = "Какие приложения сейчас выходят в сеть и каким путём")
    if (!connected) Text("Включите VPN, чтобы увидеть соединения.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    else if (rows.isEmpty()) Text("Соединений пока нет. Откройте нужное приложение и вернитесь сюда.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    rows.forEach { row ->
        QuietDivider()
        val path = when (row.kind) {
            RouteKind.VPN -> "Через VPN · ${row.exits.joinToString()}"
            RouteKind.DIRECT -> "Напрямую, мимо VPN"
            RouteKind.BLOCKED -> "Заблокировано"
        }
        val details = buildList {
            add(path)
            add("Правило: ${row.rules.joinToString()}")
            if (row.noReply > 0) add("Без ответа: ${row.noReply}")
            add("Соединений: ${row.active} · ${TrafficRows.bytes(row.uplink + row.downlink)}")
            if (row.kind != RouteKind.VPN && row.hosts.isNotEmpty()) add(row.hosts.joinToString())
        }.joinToString("\n")
        val fix = if (row.kind == RouteKind.DIRECT) TrafficRows.throughVpn(profile, row.packageName) else null
        QuietRow(
            app(row.packageName)?.label ?: row.packageName, subtitle = details,
            leading = { AppIcon(app(row.packageName)) },
            titleColor = if (row.kind == RouteKind.VPN) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.error,
            modifier = Modifier.testTag("traffic-row:${row.packageName}"),
        )
        if (fix != null) QuietTextButton(onClick = { onSave(fix) }, modifier = Modifier.testTag("traffic-vpn:${row.packageName}")) { Text("Пустить через VPN") }
        else if (row.kind == RouteKind.DIRECT) Text("Приложение идёт напрямую из-за списка сайтов или набора страны. Измените их в «Приложения и сайты».", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
    val excluded = if (profile.excludeSelectedApps) profile.apps else emptyList()
    if (excluded.isNotEmpty()) {
        CaptionLabel("Исключены из VPN")
        excluded.forEach { packageName ->
            QuietDivider()
            QuietRow(app(packageName)?.label ?: packageName, subtitle = "Идёт напрямую: приложение в списке исключений", leading = { AppIcon(app(packageName)) })
            TrafficRows.throughVpn(profile, packageName)?.let { fix ->
                QuietTextButton(onClick = { onSave(fix) }, modifier = Modifier.testTag("traffic-vpn:$packageName")) { Text("Пустить через VPN") }
            }
        }
    }
}

@Composable internal fun UsagePage(profile: ClientProfile, appChoices: List<AppChoice>) {
    val context = LocalContext.current
    var ledger by remember { mutableStateOf(TrafficMonitor.usage(context)) }
    LaunchedEffect(Unit) { while (true) { ledger = TrafficMonitor.usage(context); delay(5_000) } }
    val app = rememberApps(appChoices)
    fun label(packageName: String) = app(packageName)?.label ?: packageName
    PageHeading("Расход трафика", subtitle = "Посчитано на этом телефоне, пока включён VPN")
    val subscriptions = profile.nodes.groupBy(NodePresentation::subscriptionKey)
    if (subscriptions.isNotEmpty()) CaptionLabel("Подписки за месяц")
    subscriptions.forEach { (key, nodes) ->
        val metadata = nodes.first().subscriptionMetadata
        val used = ledger.subscriptionsMonth[key]?.total ?: 0L
        val limit = metadata.totalBytes?.takeIf { it > 0 }
        val provider = if (limit != null) "По данным подписки: ${TrafficRows.bytes(metadata.usedBytes ?: 0)} из ${TrafficRows.bytes(limit)}" else "Лимит трафика подписка не сообщает"
        QuietDivider()
        QuietRow(metadata.title ?: NodePresentation.subscriptionSource(nodes.first()), subtitle = "Через этот телефон: ${TrafficRows.bytes(used)}\n$provider", modifier = Modifier.testTag("usage-subscription:$key"))
    }
    @Composable fun section(title: String, usage: Map<String, Usage>, tag: String) {
        CaptionLabel(title)
        val top = usage.entries.sortedByDescending { it.value.total }.take(15)
        if (top.isEmpty()) Text("Пока ничего не посчитано.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        top.forEach { (packageName, value) ->
            QuietDivider()
            QuietRow(label(packageName), subtitle = "↓ ${TrafficRows.bytes(value.downlink)} · ↑ ${TrafficRows.bytes(value.uplink)}", leading = { AppIcon(app(packageName)) }, modifier = Modifier.testTag("$tag:$packageName"))
        }
    }
    section("Сегодня", ledger.appsToday, "usage-today")
    section("За месяц", ledger.appsMonth, "usage-month")
}
