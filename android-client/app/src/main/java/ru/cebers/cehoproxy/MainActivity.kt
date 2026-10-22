package ru.cebers.cehoproxy

import android.app.Activity
import android.content.BroadcastReceiver
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.graphics.drawable.Drawable
import android.net.VpnService
import android.os.Bundle
import android.provider.Settings
import android.net.Uri
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.compose.BackHandler
import androidx.activity.result.contract.ActivityResultContracts
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import androidx.compose.foundation.clickable
import androidx.compose.foundation.background
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.focusable
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.zIndex
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.graphics.vector.toPath
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.res.painterResource
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import androidx.core.graphics.drawable.toBitmap
import androidx.lifecycle.lifecycleScope
import io.nekohasekai.libbox.Libbox
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import ru.cebers.cehoproxy.core.*
import ru.cebers.cehoproxy.ui.*
import ru.cebers.cehoproxy.vpn.CehoVpnService
import ru.cebers.cehoproxy.vpn.ConnectionDiagnostics
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import java.net.URL
import java.net.URLConnection
import java.io.ByteArrayOutputStream
import java.nio.charset.StandardCharsets
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.roundToInt

private const val SUPPORT_BOT_URL = "https://t.me/CebCehVPN_bot"

/** Outcome of adding or refreshing something; shown on any screen until the user closes it or it times out. */
internal data class UiResult(val id: Int, val message: String, val error: Boolean)

class MainActivity : ComponentActivity() {
    private lateinit var store: ProfileStore
    private var profile by mutableStateOf(newInstallProfile())
    private var onboardingCompleted by mutableStateOf(false)
    private var pendingExportScope = ProfileExportScope.ALL
    private var importRevision by mutableIntStateOf(0)
    private var importing by mutableStateOf(false)
    private var connected by mutableStateOf(false)
    private var connectedSince by mutableStateOf(0L)
    private var serviceRunning by mutableStateOf(false)
    private var blocked by mutableStateOf(false)
    private var notificationsBlocked by mutableStateOf(false)
    private var measurements by mutableStateOf(VpnMeasurements())
    private var resumed = false
    private val metrics by lazy { NodeMetricsController(this) { measurements = it } }
    private var reachabilityJob: kotlinx.coroutines.Job? = null

    private fun checkNodes(routeIds: List<String>) {
        if (connected || measurements.connected) { metrics.checkLatency(routeIds); return }
        val nodes = profile.nodes.filter { it.routeId in routeIds }
        reachabilityJob?.cancel()
        measurements = measurements.copy(nodes = measurements.nodes + nodes.associate { it.routeId to NodeMetricsPolicy.beginCheck(measurements.nodes[it.routeId]) })
        reachabilityJob = lifecycleScope.launch {
            val gate = kotlinx.coroutines.sync.Semaphore(4)
            kotlinx.coroutines.coroutineScope {
                nodes.forEach { node ->
                    launch {
                        val result = gate.withPermit { withContext(Dispatchers.IO) { NodeReachability.check(node, System.currentTimeMillis()) } }
                        measurements = measurements.copy(nodes = measurements.nodes + (node.routeId to result.copy(verifiedCountry = measurements.nodes[node.routeId]?.verifiedCountry)))
                    }
                }
            }
        }
    }
    private var themePreference by mutableStateOf(ThemePreference.DARK)
    private var notice by mutableStateOf<String?>(null)
    private var result by mutableStateOf<UiResult?>(null)
    private var resultCounter = 0
    private fun report(message: String, error: Boolean = true) { result = UiResult(++resultCounter, message, error) }
    private var pendingLinkImport by mutableStateOf<String?>(null)
    private var refreshingUrls by mutableStateOf(emptySet<String>())
    private val statusReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) { refreshVpnStatus(intent?.getStringExtra("status"), intent?.getStringExtra("message")) }
    }
    private var receiverRegistered = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        store = ProfileStore(this)
        themePreference = ThemePreference.fromStorage(getSharedPreferences("appearance", MODE_PRIVATE).getString("theme", null))
        profile = store.read()
        val hasExistingSetup = profile.nodes.isNotEmpty() || !profile.importedConfig.isNullOrBlank() || profile.apps.isNotEmpty()
        onboardingCompleted = getSharedPreferences("onboarding", MODE_PRIVATE).getBoolean("completed", hasExistingSetup)
        ExpiryReminderScheduler.reconcile(this, profile)
        refreshVpnStatus()
        ContextCompat.registerReceiver(this, statusReceiver, IntentFilter(CehoVpnService.ACTION_STATUS), ContextCompat.RECEIVER_NOT_EXPORTED)
        receiverRegistered = true
        val load = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
            if (uri != null) lifecycleScope.launch {
                runCatching { withContext(Dispatchers.IO) { readTextFile(uri) } }
                    .onSuccess { importText(it) }.onFailure { report("Не удалось прочитать файл: ${it.message}") }
            }
        }
        val qrImage = registerForActivityResult(ActivityResultContracts.GetContent()) { uri ->
            if (uri != null) importQrImage(uri)
        }
        val scanQr = GmsBarcodeScanning.getClient(this, GmsBarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).allowManualInput().build())
        val export = registerForActivityResult(ActivityResultContracts.CreateDocument("application/json")) { uri ->
            if (uri != null) lifecycleScope.launch {
                runCatching { contentResolver.openOutputStream(uri)?.bufferedWriter()?.use { it.write(exportText()) } ?: error("Не удалось записать файл") }
                    .onSuccess { notice = "Резервная копия сохранена" }.onFailure { notice = "Ошибка экспорта: ${it.message}" }
            }
        }
        val vpnPermission = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
            if (result.resultCode == Activity.RESULT_OK) connect() else { notice = "Разрешение Android VPN не выдано"; report("Разрешение Android VPN не выдано. Нажмите на водоворот, чтобы запросить его снова.") }
        }
        val connectNotificationPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { _ ->
            refreshVpnStatus()
            val intent = VpnService.prepare(this)
            if (intent != null) vpnPermission.launch(intent) else connect()
        }
        val notificationPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            if (granted) update(profile.copy(expiryRemindersEnabled = true))
            else { update(profile.copy(expiryRemindersEnabled = false)); notice = "Android не разрешил уведомления. Напоминания можно включить в системных настройках приложения." }
        }
        setContent {
            CehoTheme(themePreference) {
                ClientScreen(
                    profile = profile, connected = connected, connectedSince = connectedSince, serviceRunning = serviceRunning, blocked = blocked, measurements = measurements, notificationsBlocked = notificationsBlocked, onOpenNotificationSettings = ::openNotificationSettings, onCheckNode = metrics::check, onCheckNodes = ::checkNodes, onCancelNodeCheck = metrics::cancelProbe, notice = notice, result = result, onDismissResult = { result = null }, importing = importing, importRevision = importRevision,
                    themePreference = themePreference, onThemePreference = { selected ->
                        themePreference = selected
                        getSharedPreferences("appearance", MODE_PRIVATE).edit().putString("theme", selected.storageValue).apply()
                    },
                    onboardingCompleted = onboardingCompleted,
                    onCompleteOnboarding = {
                        getSharedPreferences("onboarding", MODE_PRIVATE).edit().putBoolean("completed", true).apply()
                        onboardingCompleted = true
                    },
                    onRestartOnboarding = {
                        getSharedPreferences("onboarding", MODE_PRIVATE).edit().putBoolean("completed", false).apply()
                        onboardingCompleted = false
                    },
                    onNotice = { notice = it }, onImportFile = { load.launch(arrayOf("*/*")) },
                    onImportClipboard = { importClipboard() },
                    onScanQr = {
                        scanQr.startScan().addOnSuccessListener { code -> code.rawValue?.let(::importText) ?: run { report("В QR-коде нет текста") } }
                            .addOnFailureListener { report("Сканер недоступен. Выберите QR-изображение или вставьте ссылку из буфера обмена.") }
                    },
                    onImportQrImage = { qrImage.launch("image/*") },
                    onExport = { scope -> pendingExportScope = scope; export.launch(scope.fileName) },
                    appChoices = installedApps(),
                    onTextImport = { text -> importSubscription(text) },
                    onConfigImport = { text -> importText(text) },
                    onSelectNode = { index ->
                        if (index != profile.selected || profile.importedConfig != null) {
                            update(profile.copy(selected = index, importedConfig = null), explicitSelection = true)
                            if (VpnHomeAction.shouldStop(connected, CehoVpnService.hasLiveServiceInstance())) connect()
                        }
                    },
                    onApps = { value -> update(profile.copy(apps = value.split(',', '\n').map(String::trim).filter(String::isNotBlank))); if (VpnHomeAction.shouldStop(connected, CehoVpnService.hasLiveServiceInstance())) connect() },
                    onExcludeSelectedApps = { update(profile.copy(excludeSelectedApps = it)); if (VpnHomeAction.shouldStop(connected, CehoVpnService.hasLiveServiceInstance())) connect() },
                    onSites = { value -> update(profile.copy(sites = value.split(',', '\n').map(RoutingConfig::normalizeDomain).filter(String::isNotBlank), importedConfig = null)); if (VpnHomeAction.shouldStop(connected, CehoVpnService.hasLiveServiceInstance())) connect() },
                    onOnlySites = { update(profile.copy(onlySites = it, importedConfig = null)); if (VpnHomeAction.shouldStop(connected, CehoVpnService.hasLiveServiceInstance())) connect() },
                    onDirectCountry = { code ->
                        if (profile.importedConfig == null) {
                            update(profile.copy(directCountryCode = CountryRouting.normalize(code)))
                            if (VpnHomeAction.shouldStop(connected, CehoVpnService.hasLiveServiceInstance())) connect()
                        }
                    },
                    onSaveRouteSettings = { next -> update(next, explicitSelection = next.selected != profile.selected); if (VpnHomeAction.shouldStop(connected, CehoVpnService.hasLiveServiceInstance())) connect() },
                    onConnect = {
                        if (!hasNetwork()) notice = "Нет интернета. Подключитесь к Wi-Fi или мобильной сети"
                        else if (profile.apps.isEmpty() && !profile.excludeSelectedApps) notice = "Выберите приложения для туннеля или включите режим «Все, кроме выбранных»."
                        else if (profile.importedConfig.isNullOrBlank() && profile.selectedNode == null && LocalDpiPolicy.activeApps(profile).isEmpty()) notice = "Сначала добавьте ссылку или конфигурацию"
                        else {
                            val notificationsAsked = getSharedPreferences("onboarding", MODE_PRIVATE)
                            if (android.os.Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(this, android.Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED && !notificationsAsked.getBoolean("notifications_asked", false)) {
                                notificationsAsked.edit().putBoolean("notifications_asked", true).apply()
                                connectNotificationPermission.launch(android.Manifest.permission.POST_NOTIFICATIONS)
                            } else {
                                val intent = VpnService.prepare(this)
                                if (intent != null) vpnPermission.launch(intent) else connect()
                            }
                        }
                    }, onStop = { startService(Intent(this, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_STOP)); notice = "VPN отключается" },
                    onAlwaysOn = { openVpnSettings() },
                    onHelp = { openSupportBot() },
                    onExpiryReminders = { enabled ->
                        if (!enabled) update(profile.copy(expiryRemindersEnabled = false))
                        else if (android.os.Build.VERSION.SDK_INT >= 33 && ContextCompat.checkSelfPermission(this, android.Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) notificationPermission.launch(android.Manifest.permission.POST_NOTIFICATIONS)
                        else update(profile.copy(expiryRemindersEnabled = true))
                    },
                    onSubscriptionRefreshHours = { hours -> update(profile.copy(subscriptionRefreshHours = hours)) },
                    onOpenSubscriptionLink = { openSubscriptionLink(it) },
                    onRefreshSubscription = { url ->
                        if (url !in refreshingUrls) lifecycleScope.launch {
                            refreshingUrls = refreshingUrls + url
                            try {
                                notice = "Обновляем подписку…"
                                val fetched = withContext(Dispatchers.IO) { SubscriptionAutoRefresh.fetchAll(applicationContext, listOf(url)) }
                                if (fetched.isEmpty()) report("Подписка не ответила, прежние серверы сохранены. Проверьте интернет и попробуйте ещё раз.")
                                else {
                                    applyFetched(fetched)
                                    report("Подписка обновлена: серверов ${fetched.values.sumOf { it.size }}", error = false); notice = null
                                }
                            } finally {
                                refreshingUrls = refreshingUrls - url
                            }
                        }
                    },
                    refreshingSubscriptions = refreshingUrls,
                    onRefreshAllSubscriptions = {
                        if (refreshingUrls.isEmpty()) lifecycleScope.launch {
                            val urls = profile.subscriptionUrls
                            refreshingUrls = refreshingUrls + urls
                            try {
                                notice = "Обновляем подписки…"
                                val fetched = withContext(Dispatchers.IO) { SubscriptionAutoRefresh.fetchAll(applicationContext, urls) }
                                if (fetched.isEmpty()) report("Подписки не ответили, прежние серверы сохранены. Проверьте интернет и попробуйте ещё раз.")
                                else {
                                    applyFetched(fetched)
                                    report("Обновлено подписок: ${fetched.size} из ${urls.size}, серверов ${fetched.values.sumOf { it.size }}", error = fetched.size < urls.size); notice = null
                                }
                            } finally {
                                refreshingUrls = refreshingUrls - urls.toSet()
                            }
                        }
                    },
                    onRemoveSubscription = { url ->
                        update(SubscriptionRemovalPolicy.remove(profile, url))
                        notice = "Подписка удалена"
                        if (VpnHomeAction.shouldStop(connected, CehoVpnService.hasLiveServiceInstance())) connect()
                    },
                )
                pendingLinkImport?.let { link ->
                    AlertDialog(
                        onDismissRequest = { pendingLinkImport = null },
                        title = { Text("Добавить подписку?") },
                        text = { Text("Ссылка открыта из браузера: ${runCatching { java.net.URL(link).host }.getOrDefault(link)}. Подписка добавится в CehoProxy.") },
                        confirmButton = { TextButton(onClick = { pendingLinkImport = null; importText(link) }, modifier = Modifier.testTag("link-import-confirm")) { Text("Добавить") } },
                        dismissButton = { TextButton(onClick = { pendingLinkImport = null }) { Text("Отмена") } },
                    )
                }
            }
        }
        consumeIncomingIntent(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        consumeIncomingIntent(intent)
    }

    private fun consumeIncomingIntent(intent: Intent?) {
        if (intent?.action == Intent.ACTION_VIEW && intent.data?.scheme == "cehoproxy") {
            pendingLinkImport = intent.data?.getQueryParameter("url")?.takeIf { it.startsWith("https://") || it.startsWith("http://") }
            return
        }
        if (intent == null || intent.action !in setOf(Intent.ACTION_SEND, Intent.ACTION_PROCESS_TEXT)) return
        val text = if (intent.action == Intent.ACTION_PROCESS_TEXT) intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT)?.toString()
        else intent.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString()
        if (!text.isNullOrBlank()) importText(text.trim())
    }

    private fun importClipboard() {
        val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        val text = runCatching { clipboard.primaryClip?.getItemAt(0)?.coerceToText(this)?.toString()?.trim() }.getOrNull()
        if (text.isNullOrBlank()) report("В буфере обмена нет текста для импорта") else importText(text)
    }

    private fun readTextFile(uri: Uri): String {
        val limit = 8 * 1024 * 1024
        val output = ByteArrayOutputStream()
        contentResolver.openInputStream(uri)?.use { input ->
            val buffer = ByteArray(8192)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                require(output.size() + count <= limit) { "Файл превышает допустимый размер 8 МиБ" }
                output.write(buffer, 0, count)
            }
        } ?: error("Файл пуст")
        return String(output.toByteArray(), StandardCharsets.UTF_8).also { require(it.isNotBlank()) { "Файл пуст" } }
    }

    override fun onResume() { super.onResume(); resumed = true; refreshVpnStatus(); if (::store.isInitialized) ExpiryReminderScheduler.reconcile(this, profile) }
    override fun onPause() { resumed = false; metrics.stop(); super.onPause() }
    override fun onDestroy() { metrics.destroy(); if (receiverRegistered) unregisterReceiver(statusReceiver); super.onDestroy() }

    private fun openNotificationSettings() {
        startActivity(Intent(android.provider.Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(android.provider.Settings.EXTRA_APP_PACKAGE, packageName))
    }

    private fun refreshVpnStatus(status: String? = null, message: String? = null) {
        val prefs = getSharedPreferences("vpn_status", MODE_PRIVATE)
        val current = status ?: prefs.getString("status", "stopped")
        if (::store.isInitialized) profile = store.read().let { stored -> if (stored.nodes.map { it.raw } != profile.nodes.map { it.raw }) stored else ActiveNodeSelection.sync(profile, stored) }
        serviceRunning = current != "stopped" && VpnHomeAction.shouldStop(VpnStatusPolicy.isConnected(current, CehoVpnService.hasLiveTunnel()), CehoVpnService.hasLiveServiceInstance())
        blocked = VpnHomeAction.isBlocked(current, CehoVpnService.hasLiveServiceInstance())
        notificationsBlocked = !androidx.core.app.NotificationManagerCompat.from(this).areNotificationsEnabled()
        metrics.restoreCountries(profile)
        connected = VpnStatusPolicy.isConnected(current, CehoVpnService.hasLiveTunnel())
        connectedSince = if (connected) prefs.getLong("connected_at", 0L) else 0L
        if (connected && resumed) metrics.start(profile) else metrics.stop()
        notice = when (current) {
            "error" -> message ?: prefs.getString("message", null) ?: "Ошибка VPN"
            "starting" -> if (VpnStatusPolicy.isStarting(current, CehoVpnService.hasLiveServiceInstance())) "VPN запускается" else null
            "stopped" -> if (notice == "VPN отключается") "VPN отключён" else message ?: prefs.getString("message", null)
            else -> message
        }
    }

    @Suppress("DEPRECATION")
    private fun installedApps(): List<AppChoice> {
        val pm = packageManager
        val intents = listOf(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER), Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LEANBACK_LAUNCHER))
        return intents.flatMap { intent ->
            val resolved = if (android.os.Build.VERSION.SDK_INT >= 33) pm.queryIntentActivities(intent, PackageManager.ResolveInfoFlags.of(0)) else pm.queryIntentActivities(intent, 0)
            resolved.mapNotNull { info -> info.activityInfo?.let { AppChoice(it.packageName, info.loadLabel(pm).toString(), info.loadIcon(pm)) } }
        }.filter { it.packageName != packageName }.distinctBy { it.packageName }.sortedBy { it.label.lowercase() }
    }

    private fun importSubscription(input: String, requestOptions: SubscriptionRequestOptions = SubscriptionRequestOptions()) {
        if (importing) return
        val kind = SubscriptionInput.classify(input)
        importing = true
        notice = null
        result = null
        lifecycleScope.launch {
            try {
                val (nodes, rejectedByEngine, unsupportedTypes) = withContext(Dispatchers.IO) {
                    var metadata = SubscriptionMetadata()
                    val text =
                    if (kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION) {
                        SubscriptionTunnelFetch.fetch(applicationContext, input, requestOptions).also { metadata = it.metadata }.body
                    } else input
                    val sourceNodes = NodeShareParser.parse(text).let { if (kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION) SubscriptionNames.resolve(input, it) { u, o -> SubscriptionTunnelFetch.fetch(applicationContext, u, o).body } else it }
                    val (parsedNodes, rejected) = validateNodes(sourceNodes)
                    val fetchedMetadata = if (kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION) metadata else SubscriptionMetadata.fromBody(text)
                    val validNodes = fetchedMetadata.attachTo(parsedNodes)
                    Triple(validNodes, rejected, NodeShareParser.unsupportedTypes(text))
                }
                if (nodes.isEmpty()) {
                    report(when {
                        input.trimStart().startsWith("happ://crypt", ignoreCase = true) -> "Зашифрованную подписку Happ нельзя открыть напрямую. Попросите у провайдера обычную HTTPS-ссылку подписки или ссылку на узел."
                        rejectedByEngine > 0 -> "Узлы распознаны, но sing-box не принял их конфигурацию. Проверьте параметры ноды."
                        unsupportedTypes.isNotEmpty() -> "Не поддерживаются типы узлов: ${unsupportedTypes.joinToString()}."
                        kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION -> "В подписке нет поддерживаемых узлов"
                        else -> "Ссылка не содержит поддерживаемый узел"
                    })
                    return@launch
                }
                val mergedByRaw = profile.nodes.associateBy { it.raw }.toMutableMap()
                nodes.forEach { node ->
                    val previous = mergedByRaw[node.raw]
                    val mergedMetadata = node.subscriptionMetadata.mergeMissing(previous?.subscriptionMetadata ?: SubscriptionMetadata())
                    mergedByRaw[node.raw] = node.copy(subscriptionExpiresAt = node.subscriptionExpiresAt ?: previous?.subscriptionExpiresAt, subscriptionMetadata = mergedMetadata)
                }
                val merged = mergedByRaw.values.toList()
                val count = merged.size - profile.nodes.size
                val remote = kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION
                update(profile.copy(nodes = merged, importedConfig = null, selected = profile.selected.coerceAtMost((merged.size - 1).coerceAtLeast(0)),
                    subscriptionUrls = if (remote) (profile.subscriptionUrls + input.trim()).distinct() else profile.subscriptionUrls,
                    subscriptionFetchedAt = if (remote) System.currentTimeMillis() else profile.subscriptionFetchedAt,
                    subscriptionFetchedAtByUrl = if (remote) profile.subscriptionFetchedAtByUrl + (input.trim() to System.currentTimeMillis()) else profile.subscriptionFetchedAtByUrl))
                importRevision++
                val duplicates = nodes.size - count
                report(buildString {
                    append("Добавлено узлов: $count")
                    if (duplicates > 0) append(" · дубликатов: $duplicates")
                    if (rejectedByEngine > 0) append(" · отклонено проверкой: $rejectedByEngine")
                    if (unsupportedTypes.isNotEmpty()) append(" · не поддерживаются: ${unsupportedTypes.joinToString()}")
                }, error = count == 0 && duplicates == 0)
            } catch (error: Exception) {
                if (error is CancellationException) throw error
                report(when {
                    error is SubscriptionBodyTooLarge -> "Подписка превышает допустимый размер 8 МиБ"
                    error is SubscriptionHttpException -> "Сервер подписки вернул HTTP ${error.statusCode}"
                    error is java.net.SocketTimeoutException -> "Сервер подписки не ответил вовремя. Проверьте сеть и попробуйте ещё раз."
                    error is java.net.UnknownHostException -> "Не удалось найти сервер подписки. Проверьте подключение к интернету."
                    error is javax.net.ssl.SSLException -> "Не удалось установить защищённое соединение с сервером подписки."
                    error is IllegalArgumentException && error.message?.contains("размер") == true -> error.message
                    error.message?.contains("cleartext", ignoreCase = true) == true && error.message?.contains("not permitted", ignoreCase = true) == true -> "Незащищённая HTTP-подписка заблокирована Android. Используйте HTTPS."
                    kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION -> "Не удалось загрузить подписку: ${error.message ?: error.javaClass.simpleName}"
                    else -> "Не удалось импортировать ссылку или текст: ${error.message ?: error.javaClass.simpleName}"
                } ?: "Не удалось добавить подписку")
            } finally {
                importing = false
            }
        }
    }

    private fun importQrImage(uri: Uri) {
        lifecycleScope.launch {
            val decision = withContext(Dispatchers.IO) {
                runCatching { QrImportPolicy.decide(QrImageDecoder.decode(this@MainActivity, uri)) }
                    .getOrElse { QrImportDecision.Unsupported }
            }
            when (decision) {
                QrImportDecision.NoCode -> report("На изображении не найден QR-код")
                QrImportDecision.MultipleCodes -> report("Найдено несколько QR-кодов. Выберите изображение с одной ссылкой.")
                QrImportDecision.Unsupported -> report("QR-код не содержит поддерживаемую ссылку или подписку")
                is QrImportDecision.Import -> importSubscription(decision.value)
            }
        }
    }

    private fun readSubscriptionBody(connection: URLConnection): String {
        val limit = 8 * 1024 * 1024
        val output = ByteArrayOutputStream()
        connection.getInputStream().use { input ->
            val buffer = ByteArray(8192)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                if (output.size() + count > limit) throw SubscriptionBodyTooLarge()
                output.write(buffer, 0, count)
            }
        }
        return String(output.toByteArray(), StandardCharsets.UTF_8)
    }

    private class SubscriptionBodyTooLarge : Exception()

    private fun importText(text: String) {
        val trimmed = text.trim()
        if (!trimmed.startsWith("{")) { importSubscription(trimmed); return }
        if (importing) return
        val currentProfile = profile
        importing = true
        notice = null
        result = null
        lifecycleScope.launch {
            runCatching {
                withContext(Dispatchers.IO) {
                    val json = org.json.JSONObject(trimmed)
                    val isBackup = json.optString("format") == ProfileBackup.FORMAT
                    val backup = if (isBackup) ProfileBackup.restore(currentProfile, trimmed) else null
                    when {
                        backup != null -> backup to "Данные из резервной копии восстановлены"
                        isBackup -> error("Резервная копия повреждена")
                        json.has("outbounds") -> {
                            Libbox.checkConfig(trimmed)
                            ProfileImportPolicy.useSingBoxConfig(currentProfile, trimmed) to "Конфигурация sing-box импортирована"
                        }
                        else -> {
                            val parsed = NodeShareParser.parse(trimmed)
                            val (nodes, rejected) = validateNodes(parsed)
                            val unsupported = NodeShareParser.unsupportedTypes(trimmed)
                            require(nodes.isNotEmpty()) { if (unsupported.isNotEmpty()) "Не поддерживаются типы узлов: ${unsupported.joinToString()}" else "JSON не содержит поддерживаемых узлов" }
                            val merged = (currentProfile.nodes + nodes).distinctBy { it.raw }
                            val message = buildString {
                                append("Импортировано узлов: ${merged.size - currentProfile.nodes.size}")
                                if (rejected > 0) append(" · отклонено проверкой sing-box: $rejected")
                                if (unsupported.isNotEmpty()) append(" · не поддерживаются: ${unsupported.joinToString()}")
                            }
                            currentProfile.copy(nodes = merged, importedConfig = null) to message
                        }
                    }
                }
            }.onSuccess { (nextProfile, message) -> update(nextProfile); report(message, error = false); importRevision++ }
                .onFailure { report("Импорт не выполнен: ${it.message}") }
            importing = false
        }
    }

    private fun exportText(): String = ProfileBackup.encode(profile, pendingExportScope)
    private fun update(next: ClientProfile, explicitSelection: Boolean = false) {
        val previous = profile
        val resolved = store.update { current -> ActiveNodeSelection.beforeSave(previous, next, current, explicitSelection) }
        profile = resolved
        ExpiryReminderScheduler.reconcile(this, resolved)
        SubscriptionAutoRefresh.reconcile(this, resolved)
    }

    private fun applyFetched(fetched: Map<String, List<ProxyNode>>) {
        val resolved = SubscriptionAutoRefresh.apply(applicationContext, store, fetched)
        profile = resolved
        ExpiryReminderScheduler.reconcile(this, resolved)
        SubscriptionAutoRefresh.reconcile(this, resolved)
    }

    private fun openSubscriptionLink(value: String) {
        val uri = runCatching { Uri.parse(value) }.getOrNull() ?: return
        val allowed = when (uri.scheme?.lowercase()) {
            "https", "http" -> !uri.host.isNullOrBlank()
            "tg" -> uri.host.equals("resolve", true) || uri.host.equals("join", true)
            else -> false
        }
        if (!allowed) { report("Ссылка подписки имеет неподдерживаемый формат"); return }
        runCatching { startActivity(Intent(Intent.ACTION_VIEW, uri)) }
            .onFailure { report("Не удалось открыть ссылку. Установите Telegram или браузер.") }
    }
    private fun hasNetwork(): Boolean {
        val connectivity = getSystemService(ConnectivityManager::class.java)
        val capabilities = connectivity.activeNetwork?.let(connectivity::getNetworkCapabilities) ?: return false
        return capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    }

    private fun connect() {
        val current = profile
        val stale = if (importing || current.importedConfig != null) emptyList() else SubscriptionRefreshPolicy.staleUrls(current, System.currentTimeMillis())
        if (stale.isEmpty()) {
            startVpnService()
            return
        }
        notice = "Обновляем подписку…"
        lifecycleScope.launch {
            refreshSubscriptionNodes(current, stale)
            startVpnService()
        }
    }

    private fun startVpnService() {
        val intent = Intent(this, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_START)
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) ContextCompat.startForegroundService(this, intent)
        else startService(intent)
        notice = "VPN запускается"
    }

    /** Keeps the saved nodes when the provider is unreachable, so the tunnel still starts. */
    private suspend fun refreshSubscriptionNodes(current: ClientProfile, urls: List<String>) {
        if (urls.isEmpty()) return
        val fetched = withContext(Dispatchers.IO) { SubscriptionAutoRefresh.fetchAll(applicationContext, urls) }
        if (fetched.isEmpty()) return
        applyFetched(fetched)
    }

    private fun openVpnSettings() {
        val vpnIntent = Intent(Settings.ACTION_VPN_SETTINGS)
        val generalIntent = Intent(Settings.ACTION_SETTINGS)
        val vpnAvailable = vpnIntent.resolveActivity(packageManager) != null
        val generalAvailable = generalIntent.resolveActivity(packageManager) != null
        when (VpnSettingsActionSelector.select(vpnAvailable, generalAvailable)) {
            VpnSettingsDestination.VPN -> runCatching { startActivity(vpnIntent) }
                .onFailure { openGeneralSettings(generalIntent) }
            VpnSettingsDestination.GENERAL -> openGeneralSettings(generalIntent)
            VpnSettingsDestination.NONE -> notice = "Настройки системы недоступны. Откройте Настройки Android → Сеть и Интернет → VPN вручную."
        }
    }

    private fun openGeneralSettings(intent: Intent) {
        runCatching { startActivity(intent) }
            .onSuccess { notice = "На Android TV откройте: Сеть и Интернет → VPN → CehoProxy → Всегда включён." }
            .onFailure { notice = "Не удалось открыть настройки. На Android TV откройте: Настройки → Сеть и Интернет → VPN." }
    }

    private fun openSupportBot() {
        runCatching { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(SUPPORT_BOT_URL))) }
            .onFailure { notice = "Откройте Telegram и найдите @CebCehVPN_bot" }
    }
}

internal fun blockedTitle(notice: String?): String = when {
    notice?.startsWith("Нет интернета") == true -> "Нет интернета"
    notice?.startsWith("Подписка истекла") == true -> "Подписка истекла"
    else -> "Серверы не отвечают"
}

private fun shareEventLog(context: Context) {
    val text = EventLog.read(context).ifBlank { "Журнал пуст" }
    context.startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, text), "Поделиться журналом"))
}

@Composable private fun BrandMarkBackground(): Color {
    val colors = MaterialTheme.colorScheme
    return if (colors.background.luminance() > .5f) colors.primary else colors.primaryContainer
}

internal data class AppChoice(val packageName: String, val label: String, val icon: Drawable)

internal fun nodeAllowed(profile: ClientProfile, node: ProxyNode): Boolean =
    NodePresentation.subscriptionKey(node) !in profile.disabledSubscriptions && !RoutingConfig.countryExcluded(node, profile.excludedCountrySets)

private fun moveNodeInList(profile: ClientProfile, raw: String, steps: Int): ClientProfile {
    val from = profile.nodes.indexOfFirst { it.raw == raw }
    if (from < 0 || steps == 0) return profile
    val key = NodePresentation.subscriptionKey(profile.nodes[from])
    val sameSource = profile.nodes.indices.filter { NodePresentation.subscriptionKey(profile.nodes[it]) == key }
    val target = sameSource[(sameSource.indexOf(from) + steps).coerceIn(0, sameSource.lastIndex)]
    if (target == from) return profile
    val list = profile.nodes.toMutableList()
    list.add(target, list.removeAt(from))
    val selectedRaw = profile.selectedNode?.raw
    return profile.copy(nodes = list, selected = list.indexOfFirst { it.raw == selectedRaw }.coerceAtLeast(0))
}

@Composable private fun DraggableNodeRow(raw: String, onDragEnd: (Int) -> Unit, content: @Composable (Modifier) -> Unit) {
    var offsetY by remember(raw) { mutableFloatStateOf(0f) }
    var dragging by remember(raw) { mutableStateOf(false) }
    var rowHeight by remember(raw) { mutableFloatStateOf(1f) }
    val currentOnDragEnd by rememberUpdatedState(onDragEnd)
    Row(
        Modifier.fillMaxWidth().onSizeChanged { rowHeight = it.height.toFloat().coerceAtLeast(1f) }.zIndex(if (dragging) 1f else 0f).offset { IntOffset(0, offsetY.roundToInt()) },
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            Modifier.size(40.dp).pointerInput(raw) {
                detectDragGestures(
                    onDragStart = { dragging = true; offsetY = 0f },
                    onDragEnd = { dragging = false; currentOnDragEnd((offsetY / rowHeight).roundToInt()); offsetY = 0f },
                    onDragCancel = { dragging = false; offsetY = 0f },
                ) { change, amount -> change.consume(); offsetY += amount.y }
            }.testTag("node-drag:$raw"),
            contentAlignment = Alignment.Center,
        ) { Text("≡", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.titleLarge) }
        content(Modifier.weight(1f))
    }
}

private fun subscriptionToggled(profile: ClientProfile, key: String, enabled: Boolean): ClientProfile {
    val next = profile.copy(disabledSubscriptions = if (enabled) profile.disabledSubscriptions - key else profile.disabledSubscriptions + key)
    val selected = profile.selectedNode
    if (selected == null || nodeAllowed(next, selected)) return next
    return next.copy(selected = next.nodes.indexOfFirst { nodeAllowed(next, it) }.takeIf { it >= 0 } ?: profile.selected)
}

private fun slowNodeRouteIds(profile: ClientProfile, measurements: VpnMeasurements, limitMs: Int): List<String> {
    val anyMeasured = profile.nodes.any { measurements.nodes[it.routeId]?.latencyMs != null }
    if (!anyMeasured) return emptyList()
    return profile.nodes.map { it.routeId }.distinct().filter { id ->
        val latency = measurements.nodes[id]?.latencyMs
        latency == null || (limitMs > 0 && latency > limitMs)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable private fun ClientScreen(profile: ClientProfile, connected: Boolean, connectedSince: Long, serviceRunning: Boolean, blocked: Boolean, measurements: VpnMeasurements, notificationsBlocked: Boolean, onOpenNotificationSettings: () -> Unit, onCheckNode: (String) -> Unit, onCheckNodes: (List<String>) -> Unit, onCancelNodeCheck: () -> Unit, notice: String?, result: UiResult?, onDismissResult: () -> Unit, importing: Boolean, importRevision: Int, themePreference: ThemePreference, onThemePreference: (ThemePreference) -> Unit, onboardingCompleted: Boolean, onCompleteOnboarding: () -> Unit, onRestartOnboarding: () -> Unit, appChoices: List<AppChoice>, onNotice: (String) -> Unit, onImportFile: () -> Unit, onImportClipboard: () -> Unit, onScanQr: () -> Unit, onImportQrImage: () -> Unit, onExport: (ProfileExportScope) -> Unit, onTextImport: (String) -> Unit, onConfigImport: (String) -> Unit, onSelectNode: (Int) -> Unit, onApps: (String) -> Unit, onExcludeSelectedApps: (Boolean) -> Unit, onSites: (String) -> Unit, onOnlySites: (Boolean) -> Unit, onDirectCountry: (String) -> Unit, onSaveRouteSettings: (ClientProfile) -> Unit, onConnect: () -> Unit, onStop: () -> Unit, onAlwaysOn: () -> Unit, onHelp: () -> Unit, onExpiryReminders: (Boolean) -> Unit, onSubscriptionRefreshHours: (Int) -> Unit, onOpenSubscriptionLink: (String) -> Unit, onRefreshSubscription: (String) -> Unit, onRemoveSubscription: (String) -> Unit, onRefreshAllSubscriptions: () -> Unit, refreshingSubscriptions: Set<String>) {
    val clipboardContext = LocalContext.current
    ObserveAutomaticUpdates()
    var pendingNodeCheck by remember { mutableStateOf<String?>(null) }
    pendingNodeCheck?.let { id ->
        AlertDialog(onDismissRequest = { pendingNodeCheck = null }, title = { Text("Проверить узел?") }, text = { Text("Измерим задержку и скорость через этот узел. Тест загружает и отправляет данные до 20 секунд. Расход зависит от скорости; лучше запускать по Wi-Fi. Маршруты приложений останутся прежними.") }, confirmButton = { TextButton(onClick = { pendingNodeCheck = null; onCheckNode(id) }, modifier = Modifier.testTag("node-check-confirm")) { Text("Начать проверку") } }, dismissButton = { TextButton(onClick = { pendingNodeCheck = null }) { Text("Отмена") } })
    }
    var page by remember { mutableStateOf("Главная") }
    val pageHistory = remember { mutableStateListOf<String>() }
    fun openPage(target: String) {
        pageHistory.add(page)
        page = target
    }
    fun backPage() {
        page = pageHistory.removeLastOrNull() ?: "Главная"
    }
    val contentScroll = rememberScrollState()
    LaunchedEffect(page) { contentScroll.scrollTo(0) }
    var importValue by remember { mutableStateOf("") }
    var appsValue by remember(profile.apps) { mutableStateOf(profile.apps.joinToString(", ")) }
    var sitesValue by remember(profile.sites) { mutableStateOf(profile.sites.joinToString(", ")) }
    var removeCandidate by remember { mutableStateOf<String?>(null) }
    removeCandidate?.let { url ->
        AlertDialog(
            onDismissRequest = { removeCandidate = null },
            title = { Text("Удалить подписку?") },
            text = { Text("Её узлы пропадут из списка. Вернуть можно, добавив ссылку подписки снова.") },
            confirmButton = { TextButton(onClick = { onRemoveSubscription(url); removeCandidate = null; if (page == "Подписка") backPage() }, modifier = Modifier.testTag("subscription-remove-confirm")) { Text("Удалить", color = MaterialTheme.colorScheme.error) } },
            dismissButton = { TextButton(onClick = { removeCandidate = null }) { Text("Отмена") } },
        )
    }
    var removeNodeRaw by remember { mutableStateOf<String?>(null) }
    removeNodeRaw?.let { raw ->
        val target = profile.nodes.firstOrNull { it.raw == raw }
        AlertDialog(
            onDismissRequest = { removeNodeRaw = null },
            title = { Text("Удалить сервер?") },
            text = { Text("«${target?.let { NodePresentation.displayName(it.name) } ?: "Сервер"}» исчезнет из списка вместе с сохранёнными для него логином и паролем. Добавить его можно снова.") },
            confirmButton = { TextButton(onClick = { removeNodeRaw = null; onSaveRouteSettings(NodeRemovalPolicy.remove(profile, raw)) }, modifier = Modifier.testTag("node-remove-confirm")) { Text("Удалить", color = MaterialTheme.colorScheme.error) } },
            dismissButton = { TextButton(onClick = { removeNodeRaw = null }) { Text("Отмена") } },
        )
    }
    var shareCandidate by remember { mutableStateOf<String?>(null) }
    val shareSheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    var showAddSheet by remember { mutableStateOf(false) }
    var showSitesSheet by remember { mutableStateOf(false) }
    var subscriptionPageUrl by remember { mutableStateOf<String?>(null) }
    var detailsUrl by remember { mutableStateOf<String?>(null) }
    var expandedSubscriptionSource by remember { mutableStateOf<Triple<String, SubscriptionMetadata, Long?>?>(null) }
    var routeTab by remember { mutableIntStateOf(0) }
    var showAppPicker by remember { mutableStateOf(false) }
    var appSearch by remember { mutableStateOf("") }
    var pickerNotice by remember { mutableStateOf<String?>(null) }
    var selectedApps by remember(profile.apps) { mutableStateOf(profile.apps.toSet()) }
    var appNodeRoutes by remember(profile.appNodeRoutes) { mutableStateOf(profile.appNodeRoutes) }
    var localDpiApps by remember(profile.localDpiApps) { mutableStateOf(profile.localDpiApps) }
    var siteNodeRoutes by remember(profile.siteNodeRoutes) { mutableStateOf(profile.siteNodeRoutes) }
    var autoRotationEnabled by remember(profile.autoRotationEnabled) { mutableStateOf(profile.autoRotationEnabled) }
    var showAppRoutePicker by remember { mutableStateOf(false) }
    var routeAppSearch by remember { mutableStateOf("") }
    var nodeSearch by remember { mutableStateOf("") }
    var manualProtocol by rememberSaveable { mutableStateOf(ManualProtocol.HTTPS.name) }
    var manualName by rememberSaveable { mutableStateOf("") }
    var manualHost by rememberSaveable { mutableStateOf("") }
    var manualPort by rememberSaveable { mutableStateOf("") }
    var manualUser by rememberSaveable { mutableStateOf("") }
    var manualSni by rememberSaveable { mutableStateOf("") }
    var manualPassword by remember { mutableStateOf("") }
    var manualShowPassword by remember { mutableStateOf(false) }
    var manualError by remember { mutableStateOf<String?>(null) }
    var manualResultBaseline by remember { mutableStateOf<Int?>(null) }
    fun openManualForm() {
        manualProtocol = ManualProtocol.HTTPS.name; manualName = ""; manualHost = ""; manualPort = ""; manualUser = ""; manualSni = ""
        manualPassword = ""; manualShowPassword = false; manualError = null; manualResultBaseline = result?.id
        showAddSheet = false
        openPage("Вручную")
    }
    LaunchedEffect(importRevision) {
        if (importRevision > 0) {
            showAddSheet = false
            importValue = ""
            if (page == "Вручную") { manualPassword = ""; backPage() }
        }
    }
    var showAdvancedRoutes by remember { mutableStateOf(false) }
    var collapsedSubscriptions by remember { mutableStateOf(emptySet<String>()) }
    var showAlwaysOnHelp by remember { mutableStateOf(false) }
    val context = LocalContext.current
    var diagnostics by remember { mutableStateOf<List<ConnectionDiagnostics.Step>?>(null) }
    val scope = rememberCoroutineScope()
    var connectionRun by remember { mutableIntStateOf(0) }
    var lastAnimatedRun by remember { mutableIntStateOf(0) }
    LaunchedEffect(connected) { if (connected) connectionRun++ }
    BackHandler(enabled = page != "Главная" && !showAppPicker && !showAppRoutePicker && !showAddSheet && shareCandidate == null) { backPage() }
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
      Box(Modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize().safeDrawingPadding()) {
            Row(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.background).padding(horizontal = 24.dp, vertical = 8.dp).heightIn(min = 56.dp), verticalAlignment = Alignment.CenterVertically) {
                if (page == "Главная" || !onboardingCompleted) {
                    androidx.compose.foundation.Image(
                        painter = painterResource(R.drawable.cehoproxy_mark_dark),
                        contentDescription = "CehoProxy",
                        modifier = Modifier.size(32.dp).clip(RoundedCornerShape(10.dp)).background(BrandMarkBackground()).padding(5.dp),
                    )
                    Spacer(Modifier.width(10.dp))
                    Text("CehoProxy", fontSize = 22.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                    if (onboardingCompleted) {
                        IconButton(onClick = { showAddSheet = true }, modifier = Modifier.size(48.dp).testTag("header-add-subscription").semantics { contentDescription = "Добавить подписку" }) { PlusGlyph(MaterialTheme.colorScheme.primary) }
                        IconButton(onClick = { openPage("Настройки") }, modifier = Modifier.size(48.dp).testTag("header-settings").semantics { contentDescription = "Настройки" }) { GearGlyph(MaterialTheme.colorScheme.onSurfaceVariant) }
                    }
                } else {
                    IconButton(onClick = { backPage() }, modifier = Modifier.size(48.dp).testTag("page-back").semantics { contentDescription = "Назад" }) { BackGlyph(MaterialTheme.colorScheme.onSurface) }
                    Spacer(Modifier.width(4.dp))
                    Text("CehoProxy", fontSize = 22.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
                }
            }
            if (!onboardingCompleted) {
                FirstRunOnboarding(
                    profile = profile,
                    onPickCountry = { code -> onSaveRouteSettings(countryOnboardingChoice(profile, code)) },
                    onImportFile = onImportFile,
                    onImportClipboard = onImportClipboard,
                    onScanQr = onScanQr,
                    onHelp = onHelp,
                    onFinish = onCompleteOnboarding,
                    notice = result?.takeIf { it.error }?.message,
                    modifier = Modifier.weight(1f).fillMaxWidth(),
                )
            } else {
                Column(Modifier.weight(1f).verticalScroll(contentScroll).padding(horizontal = 24.dp).widthIn(max = 820.dp).align(Alignment.CenterHorizontally)) {
                    when (page) {
                        "Главная" -> HomeScreen(
                            profile = profile, connected = connected, connectedSince = connectedSince, serviceRunning = serviceRunning, blocked = blocked,
                            measurements = measurements, notificationsBlocked = notificationsBlocked, notice = notice.takeIf { !showAddSheet }, appChoices = appChoices,
                            onOpenNotificationSettings = onOpenNotificationSettings, onConnect = onConnect, onStop = onStop, onSelectNode = onSelectNode,
                            onOpenServers = { openPage("Серверы") }, onOpenRules = { openPage("Маршрутизация") },
                            onOpenSubscription = { url -> subscriptionPageUrl = url; openPage("Подписка") },
                            onAddSubscription = { showAddSheet = true },
                        )
                        "Подписки" -> {
                            PageHeading("Подписки", subtitle = "Автообновление, ссылки и удаление")
                            if (profile.subscriptionUrls.isEmpty()) {
                                if (profile.nodes.isEmpty()) EmptyInstruction("Подписок пока нет", "Добавьте ссылку от провайдера или импортируйте файл.")
                                else Text("Серверы добавлены ссылками: ${profile.nodes.size}. Подписок нет.", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 12.dp))
                            } else {
                                profile.subscriptionUrls.forEachIndexed { index, url ->
                                    val node = profile.nodes.firstOrNull { SubscriptionRemovalPolicy.urlFor(profile, it.subscriptionMetadata) == url }
                                    SubscriptionListItem(
                                        index = index,
                                        metadata = node?.subscriptionMetadata ?: SubscriptionMetadata(sourceHost = SubscriptionRemovalPolicy.hostOf(url)),
                                        serverCount = profile.nodes.count { SubscriptionMembership.belongs(it, url, profile.subscriptionUrls) },
                                        fetchedAt = profile.subscriptionFetchedAtByUrl[url],
                                        expiresAt = node?.let { it.subscriptionExpiresAt ?: it.subscriptionMetadata.expiresAtEpochSeconds },
                                        refreshing = url in refreshingSubscriptions,
                                        detailsShown = detailsUrl == url,
                                        onRefresh = { onRefreshSubscription(url) },
                                        onCopy = { copySubscriptionLink(clipboardContext, url); onNotice("Ссылка подписки скопирована") },
                                        onDetails = { detailsUrl = if (detailsUrl == url) null else url },
                                        onShare = { shareCandidate = url },
                                        onRemove = { removeCandidate = url },
                                        onOpenLink = onOpenSubscriptionLink,
                                        modifier = Modifier.padding(top = 12.dp),
                                    )
                                }
                                OutlinedButton(onClick = onRefreshAllSubscriptions, enabled = refreshingSubscriptions.isEmpty(), modifier = Modifier.fillMaxWidth().padding(top = 14.dp).heightIn(min = 48.dp).testTag("subscriptions-refresh-all")) {
                                    RefreshGlyph(MaterialTheme.colorScheme.primary, spinning = refreshingSubscriptions.isNotEmpty())
                                    Spacer(Modifier.width(8.dp))
                                    Text(if (refreshingSubscriptions.isEmpty()) "Обновить все подписки" else "Обновляем…")
                                }
                            }
                            QuietButton("Добавить подписку", { showAddSheet = true }, Modifier.fillMaxWidth().padding(top = 14.dp).testTag("subscriptions-add"))
                            if (profile.subscriptionUrls.isNotEmpty()) {
                                SectionLabel("АВТООБНОВЛЕНИЕ")
                                SettingsGroup {
                                    Column(Modifier.padding(16.dp)) {
                                        Text("Обновлять каждые", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface)
                                        Text("Новые узлы подтягиваются сами. Если провайдер указал свой интервал, используется он.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 2.dp))
                                        Row(Modifier.fillMaxWidth().padding(top = 10.dp).horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                            SubscriptionRefreshPolicy.INTERVAL_OPTIONS_HOURS.forEach { hours ->
                                                FilterChip(selected = profile.subscriptionRefreshHours == hours, onClick = { onSubscriptionRefreshHours(hours) }, label = { Text(if (hours == 0) "Выкл" else "$hours ч") }, modifier = Modifier.testTag("subscription-refresh-$hours").focusable())
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        "Маршрутизация" -> {
                            PageHeading("Правила подключения", subtitle = "Что идёт через VPN, а что напрямую")
                            Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surfaceContainerHigh).padding(4.dp)) {
                                listOf(false to "Только выбранные", true to "Все, кроме выбранных").forEach { (all, label) ->
                                    val chosen = profile.excludeSelectedApps == all
                                    Box(
                                        Modifier.weight(1f).heightIn(min = 48.dp).clip(RoundedCornerShape(12.dp))
                                            .background(if (chosen) MaterialTheme.colorScheme.surface else Color.Transparent)
                                            .selectable(selected = chosen, role = androidx.compose.ui.semantics.Role.RadioButton, onClick = { if (!chosen) onExcludeSelectedApps(all) })
                                            .testTag(if (all) "route-mode:all" else "route-mode:only"),
                                        contentAlignment = Alignment.Center,
                                    ) { Text(label, style = MaterialTheme.typography.labelLarge, color = if (chosen) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center) }
                                }
                            }
                            Text(if (profile.excludeSelectedApps) "Через VPN идёт всё, кроме приложений из списка. Они выходят напрямую." else "Через VPN идут только приложения из списка. Остальные выходят напрямую.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(start = 4.dp, top = 8.dp))
                            Row(Modifier.fillMaxWidth().padding(top = 14.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                listOf("Приложения", "Сайты", "Исключения").forEachIndexed { index, label ->
                                    Surface(
                                        onClick = { routeTab = index },
                                        shape = RoundedCornerShape(12.dp),
                                        color = if (routeTab == index) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceContainer,
                                        modifier = Modifier.weight(1f).heightIn(min = 42.dp).testTag("route-tab:$index"),
                                    ) {
                                        Box(Modifier.fillMaxSize().padding(vertical = 10.dp), contentAlignment = Alignment.Center) {
                                            Text(label, style = MaterialTheme.typography.labelLarge, color = if (routeTab == index) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurfaceVariant)
                                        }
                                    }
                                }
                            }
                            TextButton(onClick = { showAdvancedRoutes = !showAdvancedRoutes }, contentPadding = PaddingValues(0.dp), modifier = Modifier.padding(top = 4.dp)) { Text(if (showAdvancedRoutes) "Скрыть дополнительные правила" else "Дополнительно") }
                            when (routeTab) {
                                0 -> {
                                    val labels = profile.apps.map { name -> appChoices.firstOrNull { it.packageName == name }?.label ?: name }
                                    Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(18.dp), modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
                                        Column(Modifier.padding(18.dp)) {
                                            Text(if (profile.excludeSelectedApps) "Исключены из VPN" else "Через VPN", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface)
                                            Text(
                                                if (profile.excludeSelectedApps) "Эти приложения идут напрямую. Остальные — через VPN." else "Через VPN идут только эти приложения. Остальные — напрямую.",
                                                style = MaterialTheme.typography.bodySmall,
                                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                                modifier = Modifier.padding(top = 2.dp),
                                            )
                                            if (labels.isEmpty()) Text(if (profile.excludeSelectedApps) "Исключений нет: через VPN пойдут все приложения." else "Список пуст: через VPN не пойдёт ни одно приложение.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 10.dp))
                                            else Row(Modifier.fillMaxWidth().padding(top = 10.dp).horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                                labels.forEach { label ->
                                                    Surface(shape = RoundedCornerShape(15.dp), color = MaterialTheme.colorScheme.surfaceContainerHigh) {
                                                        Text(label, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp))
                                                    }
                                                }
                                            }
                                            PillButton("Изменить список", { selectedApps = profile.apps.toSet(); pickerNotice = null; showAppPicker = true }, Modifier.fillMaxWidth().padding(top = 12.dp).testTag("home-app-picker"))
                                        }
                                    }
                                    if (showAdvancedRoutes) DirectDpiSettingsSection(profile, appChoices, onSaveRouteSettings)
                                    if (showAdvancedRoutes && profile.nodes.size > 1) {
                                        Text("ВЫХОД ДЛЯ ПРИЛОЖЕНИЙ", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 18.dp, bottom = 6.dp))
                                        val configuredApps = appsValue.split(',', '\n').map(String::trim).filter(String::isNotBlank).distinct()
                                        val assignedPackages = (if (profile.excludeSelectedApps) appNodeRoutes.keys.filterNot { it in configuredApps } else configuredApps + appNodeRoutes.keys).distinct()
                                        if (!profile.excludeSelectedApps) Text("Назначение отдельного узла также включает приложение в выбранный туннель.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(bottom = 6.dp))
                                        if (assignedPackages.isEmpty()) Text("Для отдельного выхода назначьте приложение ниже.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                        assignedPackages.forEach { packageName ->
                                            RouteAssignmentRow(
                                                title = appChoices.firstOrNull { it.packageName == packageName }?.label ?: packageName,
                                                icon = appChoices.firstOrNull { it.packageName == packageName }?.icon,
                                                nodes = profile.nodes.filter { nodeAllowed(profile, it) },
                                                selectedRaw = appNodeRoutes[packageName],
                                                onSelect = { routeId ->
                                                    appNodeRoutes = if (routeId == null) appNodeRoutes - packageName else appNodeRoutes + (packageName to routeId)
                                                    localDpiApps = localDpiApps - packageName
                                                    onSaveRouteSettings(profile.copy(appNodeRoutes = appNodeRoutes.filterKeys { it !in localDpiApps }))
                                                },
                                            )
                                        }
                                        OutlinedButton(onClick = { routeAppSearch = ""; showAppRoutePicker = true }, modifier = Modifier.fillMaxWidth().padding(top = 6.dp).heightIn(min = 48.dp)) { Text("＋ Назначить отдельное приложение") }
                                    }
                                }
                                1 -> {
                                    SettingsGroup {
                                        SettingsRow(
                                            title = "Через VPN только указанные сайты",
                                            switchChecked = profile.onlySites,
                                            onSwitch = onOnlySites,
                                            modifier = Modifier.testTag("sites-mode"),
                                        )
                                    }
                                    Text(if (profile.onlySites) "Сайты из списка идут через VPN, остальные — напрямую." else "Сайты из списка идут напрямую, остальные — через VPN.", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(start = 6.dp, top = 6.dp, bottom = 8.dp))
                                    Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(18.dp), modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
                                        Column(Modifier.padding(18.dp)) {
                                            Text(if (profile.onlySites) "Сайты через VPN" else "Сайты напрямую", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface)
                                            Text("Сайтов: ${profile.sites.size}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 2.dp))
                                            if (profile.sites.isNotEmpty()) Row(Modifier.fillMaxWidth().padding(top = 10.dp).horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                                profile.sites.take(5).forEach { site ->
                                                    Surface(shape = RoundedCornerShape(15.dp), color = MaterialTheme.colorScheme.surfaceContainerHigh) {
                                                        Text(site, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp))
                                                    }
                                                }
                                                if (profile.sites.size > 5) Text("+${profile.sites.size - 5}", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(start = 6.dp, top = 6.dp))
                                            }
                                            PillButton("Открыть список", { showSitesSheet = true }, Modifier.fillMaxWidth().padding(top = 12.dp).testTag("sites-open"), enabled = profile.importedConfig == null)
                                        }
                                    }
                                    OutlinedButton(onClick = {
                                        val fresh = DirectSitePresets.newHosts(profile.sites, DirectSitePresets.forMode(profile.onlySites))
                                        if (fresh.isEmpty()) onNotice("Эти сайты уже в списке.") else {
                                            onSites((profile.sites + fresh).joinToString(", "))
                                            onNotice("Добавлено сайтов: ${fresh.size}.")
                                        }
                                    }, enabled = profile.importedConfig == null, modifier = Modifier.padding(top = 8.dp).fillMaxWidth().heightIn(min = 48.dp).testTag("sites-preset")) { Text(if (profile.onlySites) "Добавить популярные сайты" else "Добавить российские сервисы") }
                                    if (showAdvancedRoutes && profile.nodes.size > 1) {
                                        Text("ВЫХОД ДЛЯ САЙТОВ", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 14.dp, bottom = 6.dp))
                                        val domains = sitesValue.split(',', '\n').map(RoutingConfig::normalizeDomain).filter(String::isNotBlank).distinct()
                                        if (domains.isEmpty()) Text("Добавьте домены выше, чтобы назначить им отдельный узел.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                        domains.forEach { domain ->
                                            RouteAssignmentRow(
                                                title = domain,
                                                nodes = profile.nodes.filter { nodeAllowed(profile, it) },
                                                selectedRaw = siteNodeRoutes[domain],
                                                onSelect = { routeId -> siteNodeRoutes = if (routeId == null) siteNodeRoutes - domain else siteNodeRoutes + (domain to routeId) },
                                            )
                                        }
                                    }
                                }
                                else -> {
                                    CountryRouteSelector(profile, onDirectCountry, Modifier.padding(top = 8.dp))
                                    if (showAdvancedRoutes && profile.nodes.size > 1) {
                                        Row(Modifier.fillMaxWidth().padding(top = 18.dp), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                                            Column(Modifier.weight(1f).padding(end = 8.dp)) {
                                                Text("Автоматически выбирать узел", color = MaterialTheme.colorScheme.onSurface)
                                                Text("Проверяем узлы по порядку подписки и подключаем первый рабочий.", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
                                            }
                                            Switch(checked = autoRotationEnabled, onCheckedChange = { autoRotationEnabled = it }, modifier = Modifier.testTag("rules-auto-node-switch"))
                                        }
                                        if (autoRotationEnabled) {
                                            Text("Проверка доступности примерно каждые 30 секунд. При сбое проверяется следующий узел по порядку. Отдельные выходы приложений и сайтов сохраняются.", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 4.dp))
                                            val measuredCount = profile.nodes.count { measurements.nodes[it.routeId]?.latencyMs != null }
                                            Text("Исключено из автовыбора: ${profile.nodes.count { n -> n.routeId in profile.autoExcludedRouteIds || RoutingConfig.countryExcluded(n, profile.excludedCountrySets) }} из ${profile.nodes.size}", color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.padding(top = 12.dp))
                                            Text(if (measuredCount == 0) "Замер идёт через VPN: подождите минуту после подключения." else "Исключаются узлы медленнее порога и узлы, которые не ответили при замере.", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 4.dp))
                                            Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                                listOf(300, 500, 1000).forEach { limit ->
                                                    val excluded = slowNodeRouteIds(profile, measurements, limit)
                                                    OutlinedButton(onClick = { onSaveRouteSettings(profile.copy(autoExcludedRouteIds = excluded)) }, enabled = measuredCount > 0 && excluded.size in 1 until profile.nodes.size, modifier = Modifier.weight(1f).heightIn(min = 48.dp).testTag("auto-exclude-$limit")) { Text("Медленнее $limit мс") }
                                                }
                                            }
                                            OutlinedButton(onClick = { onSaveRouteSettings(profile.copy(autoExcludedRouteIds = emptyList(), excludedCountriesBySubscription = emptyMap())) }, enabled = profile.autoExcludedRouteIds.isNotEmpty() || profile.excludedCountriesBySubscription.isNotEmpty(), modifier = Modifier.fillMaxWidth().padding(top = 8.dp).heightIn(min = 48.dp).testTag("auto-exclude-reset")) { Text("Вернуть все узлы в автовыбор") }
                                        }
                                    }
                                }
                            }
                            QuietButton("Сохранить и применить", {
                                val nextApps = appsValue.split(',', '\n').map(String::trim).filter(String::isNotBlank).distinct()
                                val enteredSites = if (profile.importedConfig == null) sitesValue.split(',', '\n').map(RoutingConfig::normalizeDomain).filter(String::isNotBlank) else profile.sites
                                val nextSites = (enteredSites + siteNodeRoutes.keys.map(RoutingConfig::normalizeDomain)).distinct()
                                onSaveRouteSettings(profile.copy(
                                    apps = nextApps,
                                    sites = nextSites,
                                    appNodeRoutes = (if (profile.excludeSelectedApps) appNodeRoutes.filterKeys { it !in nextApps } else appNodeRoutes).filterKeys { it !in localDpiApps },
                                    localDpiApps = localDpiApps,
                                    siteNodeRoutes = siteNodeRoutes.filterKeys { RoutingConfig.normalizeDomain(it) in nextSites },
                                    autoRotationEnabled = autoRotationEnabled && profile.nodes.size > 1,
                                    autoRotationIntervalSeconds = profile.autoRotationIntervalSeconds,
                                ))
                                onNotice(if (profile.importedConfig == null) "Правила сохранены" else "Правила сохранены; часть настроек импортированной конфигурации может иметь приоритет")
                            }, Modifier.padding(top = 16.dp).fillMaxWidth().testTag("routing-save"))
                            if (notice != null && !showAddSheet) Text(notice, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(top = 10.dp))
                        }
                        "Подписка" -> {
                            val url = subscriptionPageUrl?.takeIf { it in profile.subscriptionUrls }
                            if (url == null) {
                                PageHeading("Подписка")
                                EmptyInstruction("Подписка не найдена", "Откройте список подписок и выберите её снова.")
                            }
                            else {
                                val node = profile.nodes.firstOrNull { SubscriptionRemovalPolicy.urlFor(profile, it.subscriptionMetadata) == url }
                                val refreshing = url in refreshingSubscriptions
                                PageHeading("Подписка", subtitle = "Сведения, страны и обновление")
                                Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(18.dp), modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
                                    SubscriptionCard(
                                        metadata = node?.subscriptionMetadata ?: SubscriptionMetadata(sourceHost = SubscriptionRemovalPolicy.hostOf(url)),
                                        expiresAt = node?.let { it.subscriptionExpiresAt ?: it.subscriptionMetadata.expiresAtEpochSeconds },
                                        fetchedAt = profile.subscriptionFetchedAtByUrl[url],
                                        serverCount = profile.nodes.count { SubscriptionMembership.belongs(it, url, profile.subscriptionUrls) },
                                        onOpenLink = onOpenSubscriptionLink,
                                        onShare = { shareCandidate = url },
                                        onSettings = null,
                                        modifier = Modifier.padding(18.dp),
                                    )
                                }
                                profile.nodes.filter { SubscriptionMembership.belongs(it, url, profile.subscriptionUrls) }.firstOrNull()?.let { member ->
                                    val key = NodePresentation.subscriptionKey(member)
                                    val countries = profile.nodes.filter { NodePresentation.subscriptionKey(it) == key }
                                        .mapNotNull { NodeCountry.resolve(it.name) }.groupingBy { it }.eachCount().toList().sortedByDescending { it.second }
                                    val excludedHere = profile.excludedCountriesBySubscription[key].orEmpty()
                                    SectionLabel("СТРАНЫ ЭТОЙ ПОДПИСКИ")
                                    Text("Выключенные страны не используются в этой подписке. Остальные серверы работают как обычно.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(bottom = 8.dp))
                                    if (countries.isEmpty()) Text("В подписке не определены страны.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                                    else Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                        countries.forEach { (code, count) ->
                                            val excluded = code in excludedHere
                                            OutlinedButton(
                                                onClick = {
                                                    val nextHere = if (excluded) excludedHere - code else excludedHere + code
                                                    val next = profile.copy(excludedCountriesBySubscription = (profile.excludedCountriesBySubscription + (key to nextHere)).filterValues { it.isNotEmpty() })
                                                    val keep = profile.selectedNode?.let { nodeAllowed(next, it) } ?: true
                                                    onSaveRouteSettings(if (keep) next else next.copy(selected = next.nodes.indexOfFirst { nodeAllowed(next, it) }.takeIf { it >= 0 } ?: profile.selected))
                                                },
                                                modifier = Modifier.heightIn(min = 40.dp).testTag("sub-country:$code"),
                                                colors = if (excluded) ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error) else ButtonDefaults.outlinedButtonColors(),
                                            ) { Text((if (excluded) "Вернуть " else "Выключить ") + "${NodePresentation.flag(code) ?: ""} ${NodeCountry.displayName(code)} · $count") }
                                        }
                                    }
                                }
                                SectionLabel("ОБНОВЛЕНИЕ")
                                SettingsGroup {
                                    SettingsRow(
                                        title = "Автообновление",
                                        value = when {
                                            profile.subscriptionRefreshHours == 0 -> "Выкл"
                                            else -> SubscriptionRefreshPolicy.providerHours(profile, url)?.let { "каждые $it ч · от провайдера" } ?: "каждые ${profile.subscriptionRefreshHours} ч"
                                        },
                                        onClick = {
                                            val options = SubscriptionRefreshPolicy.INTERVAL_OPTIONS_HOURS
                                            onSubscriptionRefreshHours(options[(options.indexOf(profile.subscriptionRefreshHours) + 1) % options.size])
                                        },
                                    )
                                    SettingsRow(title = if (refreshing) "Обновляем…" else "Обновить сейчас", onClick = { if (!refreshing) onRefreshSubscription(url) })
                                    SettingsRow(title = "Напоминать о сроке", switchChecked = profile.expiryRemindersEnabled, onSwitch = onExpiryReminders)
                                }
                                SectionLabel("СЕРВЕРЫ")
                                SettingsGroup {
                                    node?.let { n ->
                                        val key = NodePresentation.subscriptionKey(n)
                                        SettingsRow(title = "Использовать подписку", switchChecked = key !in profile.disabledSubscriptions, onSwitch = { enabled -> onSaveRouteSettings(subscriptionToggled(profile, key, enabled)) })
                                    }
                                    SettingsRow(title = "Проверка задержки", onClick = { openPage("Серверы") })
                                }
                                SectionLabel("ДОСТУП")
                                SettingsGroup {
                                    SettingsRow(title = "Ссылка подписки", value = "•••", onClick = { copySubscriptionLink(clipboardContext, url); onNotice("Ссылка подписки скопирована") })
                                }
                                Surface(onClick = { removeCandidate = url }, color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(18.dp), modifier = Modifier.fillMaxWidth().padding(top = 18.dp).testTag("subscription-remove")) {
                                    Box(Modifier.fillMaxWidth().padding(vertical = 14.dp), contentAlignment = Alignment.Center) {
                                        Text("Удалить подписку", color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyLarge)
                                    }
                                }
                            }
                        }
                        "Серверы" -> {
                            val sourceCount = profile.subscriptionUrls.size
                            PageHeading("Ваши серверы", subtitle = listOfNotNull(
                                when (sourceCount) { 0 -> null; 1 -> "Одна подписка"; else -> "Подписок: $sourceCount" },
                                "${profile.nodes.size} ${SubscriptionSummary.plural(profile.nodes.size.toLong(), "сервер", "сервера", "серверов")}",
                            ).joinToString(" · "))
                            val allRouteIds = profile.nodes.map { it.routeId }
                            if (profile.nodes.isEmpty()) EmptyInstruction("Серверов пока нет", "Добавьте подписку или ссылку от провайдера.")
                            else {
                                OutlinedTextField(value = nodeSearch, onValueChange = { nodeSearch = it }, placeholder = { Text("Найти страну или сервер") }, singleLine = true, shape = MaterialTheme.shapes.large, modifier = Modifier.fillMaxWidth().testTag("subscriptions-search"))
                                if (profile.nodes.size > 1) {
                                    QuietDivider(Modifier.padding(top = 16.dp))
                                    QuietRow(
                                        "Автопереключение",
                                        subtitle = if (profile.autoRotationEnabled) "При сбое выберем следующий рабочий сервер, в том числе из других подписок" else "Выключено: используется только выбранный сервер",
                                        trailing = { Switch(checked = profile.autoRotationEnabled, onCheckedChange = { on -> onSaveRouteSettings(profile.copy(autoRotationEnabled = on)) }, modifier = Modifier.testTag("servers-auto-switch").semantics { contentDescription = "Автоматическое переключение" }) },
                                    )
                                    QuietDivider()
                                }
                                profile.nodes.withIndex().filter { nodeAllowed(profile, it.value) && measurements.nodes[it.value.routeId]?.latencyMs != null && measurements.nodes[it.value.routeId]?.failed != true }
                                    .minByOrNull { measurements.nodes[it.value.routeId]?.latencyMs ?: Int.MAX_VALUE }?.let { fastest ->
                                    QuietButton("Самый быстрый · ${measurements.nodes[fastest.value.routeId]?.latencyMs} мс", { onSelectNode(fastest.index) }, Modifier.fillMaxWidth().padding(top = 12.dp).testTag("subscriptions-fastest"), primary = false)
                                }
                                if (notice != null && !showAddSheet) Text(notice, color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 12.dp))
                                val visibleNodes = profile.nodes.withIndex().filter { it.value.name.contains(nodeSearch, true) || NodePresentation.subscriptionSource(it.value).contains(nodeSearch, true) }
                                visibleNodes.forEachIndexed { position, indexed ->
                                    val (index, node) = indexed
                                    val source = NodePresentation.subscriptionSource(node)
                                    val groupKey = Triple(source, node.subscriptionMetadata, node.subscriptionExpiresAt)
                                    val subscriptionKey = NodePresentation.subscriptionKey(node)
                                    val previousNode = visibleNodes.getOrNull(position - 1)?.value
                                    val previousGroupKey = previousNode?.let { Triple(NodePresentation.subscriptionSource(it), it.subscriptionMetadata, it.subscriptionExpiresAt) }
                                    if (groupKey != previousGroupKey) {
                                        val groupRouteIds = profile.nodes.filter { NodePresentation.subscriptionKey(it) == subscriptionKey }.map { it.routeId }
                                        val groupUrl = SubscriptionRemovalPolicy.urlFor(profile, node.subscriptionMetadata)
                                        val groupRefreshing = groupUrl != null && groupUrl in refreshingSubscriptions
                                        Column(Modifier.fillMaxWidth().padding(top = 24.dp)) {
                                            Row(verticalAlignment = Alignment.CenterVertically) {
                                                Column(Modifier.weight(1f)) {
                                                    Text(source.removePrefix("Подписка: "), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                                    groupUrl?.let { url -> profile.subscriptionFetchedAtByUrl[url]?.let { at -> Text(SubscriptionRefreshPolicy.fetchedLabel(at, System.currentTimeMillis()), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.testTag("subscription-fetched:$index")) } }
                                                }
                                                if (groupUrl != null) TextButton(onClick = { subscriptionPageUrl = groupUrl; openPage("Подписка") }, modifier = Modifier.heightIn(min = 48.dp).testTag("subscription-manage:$index")) { Text("Управлять ›") }
                                            }
                                            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState())) {
                                                TextButton(onClick = { expandedSubscriptionSource = if (expandedSubscriptionSource == groupKey) null else groupKey }, modifier = Modifier.heightIn(min = 48.dp).testTag("subscription-details-toggle:$index")) { Text(if (expandedSubscriptionSource == groupKey) "Скрыть сведения" else "Сведения") }
                                                TextButton(onClick = { collapsedSubscriptions = if (subscriptionKey in collapsedSubscriptions) collapsedSubscriptions - subscriptionKey else collapsedSubscriptions + subscriptionKey }, modifier = Modifier.heightIn(min = 48.dp).testTag("subscription-collapse:$index")) { Text(if (subscriptionKey in collapsedSubscriptions) "Развернуть" else "Свернуть") }
                                                TextButton(onClick = { onCheckNodes(groupRouteIds) }, enabled = groupRouteIds.isNotEmpty(), modifier = Modifier.heightIn(min = 48.dp).testTag("subscription-check-group:$subscriptionKey")) { Text("Проверить задержку") }
                                                TextButton(onClick = { groupUrl?.let(onRefreshSubscription) }, enabled = groupUrl != null && !groupRefreshing, modifier = Modifier.heightIn(min = 48.dp).testTag("subscription-refresh-group:$subscriptionKey")) { Text(if (groupRefreshing) "Обновляем…" else "Обновить") }
                                            }
                                            QuietDivider()
                                            QuietRow(
                                                "Использовать подписку", modifier = Modifier.heightIn(min = 52.dp),
                                                trailing = { Switch(checked = subscriptionKey !in profile.disabledSubscriptions, onCheckedChange = { enabled -> onSaveRouteSettings(subscriptionToggled(profile, subscriptionKey, enabled)) }, modifier = Modifier.testTag("subscription-enabled:$index").semantics { contentDescription = "Использовать подписку" }) },
                                            )
                                            if (profile.autoRotationEnabled && profile.nodes.size > 1) QuietRow(
                                                "Автопереключение в этой подписке", modifier = Modifier.heightIn(min = 52.dp),
                                                trailing = { Switch(checked = subscriptionKey !in profile.autoDisabledSubscriptions, onCheckedChange = { on -> onSaveRouteSettings(profile.copy(autoDisabledSubscriptions = if (on) profile.autoDisabledSubscriptions - subscriptionKey else profile.autoDisabledSubscriptions + subscriptionKey)) }, enabled = subscriptionKey !in profile.disabledSubscriptions, modifier = Modifier.testTag("subscription-auto:$index").semantics { contentDescription = "Автопереключение в этой подписке" }) },
                                            )
                                            QuietDivider()
                                        }
                                        if (expandedSubscriptionSource == groupKey) SubscriptionDetails(node.subscriptionMetadata, onOpenSubscriptionLink)
                                    }
                                    if (subscriptionKey !in collapsedSubscriptions) DraggableNodeRow(node.raw, onDragEnd = { steps -> onSaveRouteSettings(moveNodeInList(profile, node.raw, steps)) }) { rowModifier ->
                                        NodeRow(node, index == profile.selected, { if (nodeAllowed(profile, node)) onSelectNode(index) }, measurements.nodes[node.routeId], if (measurements.connected && profile.importedConfig == null) ({ if (measurements.nodes[node.routeId]?.testing == true) onCancelNodeCheck() else pendingNodeCheck = node.routeId }) else null, rowModifier.testTag("home-node-option:$index"),
                                            autoEnabled = node.routeId !in profile.autoExcludedRouteIds,
                                            onAutoToggle = if (!profile.autoRotationEnabled) null else ({ on: Boolean -> onSaveRouteSettings(profile.copy(autoExcludedRouteIds = if (on) profile.autoExcludedRouteIds - node.routeId else profile.autoExcludedRouteIds + node.routeId)) }),
                                            autoSwitchTag = "node-auto-switch:$index",
                                            onRemove = if (NodeRemovalPolicy.removable(profile, node)) ({ removeNodeRaw = node.raw }) else null, removeTag = "node-remove:$index",
                                            excludedLabel = when {
                                                NodePresentation.subscriptionKey(node) in profile.disabledSubscriptions -> "Подписка отключена"
                                                RoutingConfig.countryExcluded(node, profile.excludedCountrySets) -> "Страна исключена"
                                                else -> null
                                            })
                                    }
                                }
                                if (visibleNodes.isEmpty()) Text("Ничего не найдено. Измените запрос.", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(vertical = 20.dp))
                            }
                            QuietButtonRow(Modifier.padding(top = 24.dp)) {
                                QuietButton("Проверить", { onCheckNodes(allRouteIds) }, Modifier.weight(1f).testTag("servers-check-all"), primary = false, enabled = allRouteIds.isNotEmpty(), leading = { RefreshGlyph(MaterialTheme.colorScheme.onSurface) })
                                QuietButton("Добавить подписку", { showAddSheet = true }, Modifier.weight(1f).testTag("servers-add"))
                            }
                            Text(
                                (if (!measurements.connected) "Без VPN проверяется только TCP-задержка до адреса сервера, узлы Hysteria2 (UDP) проверяются при включённом VPN. " else "") + "Проверка скорости запускается отдельно, с предупреждением о расходе трафика.",
                                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 10.dp),
                            )
                            QuietDivider(Modifier.padding(top = 16.dp))
                            QuietRow("Подписки и автообновление", subtitle = "Интервал, обновление всех подписок, удаление", onClick = { openPage("Подписки") }, modifier = Modifier.testTag("settings-subscriptions"))
                            QuietDivider()
                        }
                        "Настройки" -> {
                            val primary = MaterialTheme.colorScheme.primary
                            PageHeading("Настройки")
                            CaptionLabel("Основное")
                            QuietDivider()
                            QuietRow("Серверы и подписки", subtitle = "Выбор сервера и управление подписками", onClick = { openPage("Серверы") }, leading = { QuietBadge { ServersGlyph(primary) } }, modifier = Modifier.testTag("settings-latency"))
                            QuietDivider()
                            QuietRow("Приложения и сайты", subtitle = "Что подключать через VPN", onClick = { openPage("Маршрутизация") }, leading = { QuietBadge { SlidersGlyph(primary) } }, modifier = Modifier.testTag("settings-routing"))
                            QuietDivider()
                            QuietRow("Постоянный VPN", subtitle = "Автозапуск средствами Android", onClick = { showAlwaysOnHelp = !showAlwaysOnHelp }, leading = { QuietBadge { ShieldGlyph(primary) } }, modifier = Modifier.testTag("settings-always-on"))
                            if (showAlwaysOnHelp) {
                                Text("Android включит VPN сам после перезагрузки телефона.", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(bottom = 8.dp))
                                QuietButton("Открыть настройки Android", onAlwaysOn, Modifier.fillMaxWidth().padding(bottom = 12.dp), primary = false)
                            }
                            QuietDivider()
                            QuietRow("Внешний вид", subtitle = themePreference.label, onClick = { openPage("Внешний вид") }, leading = { QuietBadge { AppearanceGlyph(primary) } }, modifier = Modifier.testTag("settings-appearance"))
                            QuietDivider()
                            CaptionLabel("Сервис")
                            QuietDivider()
                            QuietRow("Проверка и помощь", subtitle = "Соединение, журнал, поддержка", onClick = { openPage("Проверка и помощь") }, leading = { QuietBadge { HelpGlyph(primary) } }, modifier = Modifier.testTag("settings-help"))
                            QuietDivider()
                            QuietRow("Перенос настроек", subtitle = "Импорт и экспорт", onClick = { openPage("Перенос настроек") }, leading = { QuietBadge { DocumentGlyph(primary) } }, modifier = Modifier.testTag("settings-transfer"))
                            QuietDivider()
                            QuietRow("Дополнительные параметры", subtitle = "Редкие и технические настройки", onClick = { openPage("Дополнительные параметры") }, leading = { QuietBadge { SlidersGlyph(primary) } }, modifier = Modifier.testTag("settings-advanced"))
                            QuietDivider()
                            CaptionLabel("О приложении")
                            UpdateSettingsSection()
                        }
                        "Вручную" -> {
                            val protocol = ManualProtocol.valueOf(manualProtocol)
                            val fieldShape = MaterialTheme.shapes.large
                            PageHeading("Ручное подключение", subtitle = "Свой сервер или корпоративный прокси")
                            Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(MaterialTheme.colorScheme.surfaceContainerHigh).padding(4.dp)) {
                                ManualProtocol.entries.forEach { option ->
                                    val chosen = option == protocol
                                    Box(
                                        Modifier.weight(1f).heightIn(min = 48.dp).clip(RoundedCornerShape(12.dp))
                                            .background(if (chosen) MaterialTheme.colorScheme.surface else Color.Transparent)
                                            .selectable(selected = chosen, role = androidx.compose.ui.semantics.Role.RadioButton, onClick = { manualProtocol = option.name; manualError = null })
                                            .testTag("manual-protocol:${option.name}"),
                                        contentAlignment = Alignment.Center,
                                    ) { Text(option.label, style = MaterialTheme.typography.labelLarge, color = if (chosen) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center) }
                                }
                            }
                            Text(
                                when (protocol) {
                                    ManualProtocol.HTTPS -> "Прокси по HTTPS с логином и паролем (NaiveProxy, Caddy). Порт обычно 443 или 8443."
                                    ManualProtocol.SOCKS5 -> "Обычный SOCKS5-прокси. Логин и пароль нужны, только если их требует сервер."
                                    ManualProtocol.HTTP -> "HTTP-прокси: до сервера данные не шифруются. Подходит для доверенной сети."
                                },
                                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(start = 4.dp, top = 8.dp, bottom = 8.dp),
                            )
                            OutlinedTextField(value = manualName, onValueChange = { manualName = it }, label = { Text("Название (необязательно)") }, singleLine = true, shape = fieldShape, enabled = !importing, modifier = Modifier.fillMaxWidth().padding(top = 8.dp).testTag("manual-name"))
                            OutlinedTextField(value = manualHost, onValueChange = { manualHost = it; manualError = null }, label = { Text("Адрес сервера") }, placeholder = { Text("proxy.example.com или 203.0.113.5") }, singleLine = true, shape = fieldShape, enabled = !importing, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri), modifier = Modifier.fillMaxWidth().padding(top = 8.dp).testTag("manual-host"))
                            OutlinedTextField(value = manualPort, onValueChange = { v -> manualPort = v.filter(Char::isDigit).take(5); manualError = null }, label = { Text("Порт") }, placeholder = { Text(protocol.defaultPort.toString()) }, singleLine = true, shape = fieldShape, enabled = !importing, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), modifier = Modifier.fillMaxWidth().padding(top = 8.dp).testTag("manual-port"))
                            OutlinedTextField(value = manualUser, onValueChange = { manualUser = it; manualError = null }, label = { Text(if (protocol.loginRequired) "Логин" else "Логин (необязательно)") }, singleLine = true, shape = fieldShape, enabled = !importing, modifier = Modifier.fillMaxWidth().padding(top = 8.dp).testTag("manual-user"))
                            OutlinedTextField(
                                value = manualPassword, onValueChange = { manualPassword = it; manualError = null }, label = { Text(if (protocol.loginRequired) "Пароль" else "Пароль (необязательно)") }, singleLine = true, shape = fieldShape, enabled = !importing,
                                visualTransformation = if (manualShowPassword) androidx.compose.ui.text.input.VisualTransformation.None else androidx.compose.ui.text.input.PasswordVisualTransformation(),
                                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                                trailingIcon = { TextButton(onClick = { manualShowPassword = !manualShowPassword }, modifier = Modifier.heightIn(min = 48.dp).testTag("manual-password-toggle")) { Text(if (manualShowPassword) "Скрыть" else "Показать") } },
                                modifier = Modifier.fillMaxWidth().padding(top = 8.dp).testTag("manual-password"),
                            )
                            if (protocol == ManualProtocol.HTTPS) {
                                OutlinedTextField(value = manualSni, onValueChange = { manualSni = it }, label = { Text("Имя сервера для TLS (SNI)") }, placeholder = { Text("по умолчанию — адрес сервера") }, singleLine = true, shape = fieldShape, enabled = !importing, modifier = Modifier.fillMaxWidth().padding(top = 8.dp).testTag("manual-sni"))
                            }
                            val shownError = manualError ?: result?.takeIf { it.error && it.id != manualResultBaseline && !importing }?.message
                            if (importing) {
                                LinearProgressIndicator(modifier = Modifier.fillMaxWidth().padding(top = 12.dp).testTag("manual-progress"))
                                Text("Проверяем настройки сервера…", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp))
                            } else shownError?.let { Text(it, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(top = 12.dp).testTag("manual-error")) }
                            QuietButton("Добавить сервер", {
                                val form = ManualConnection(protocol, manualHost, manualPort, manualUser, manualPassword, manualSni, manualName)
                                val problem = ManualConnectionPolicy.validate(form)
                                manualError = problem
                                if (problem == null) onTextImport(ManualConnectionPolicy.toUri(form))
                            }, Modifier.fillMaxWidth().padding(top = 16.dp).testTag("manual-submit"), enabled = !importing && manualHost.isNotBlank())
                            Text("Поддерживаются прокси HTTPS (Naive), SOCKS5 и HTTP. OpenVPN, WireGuard и IPsec приложение не поддерживает.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 12.dp))
                        }
                        "Внешний вид" -> {
                            PageHeading("Внешний вид", subtitle = "Тема оформления")
                            Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                ThemePreference.entries.forEach { option ->
                                    FilterChip(selected = themePreference == option, onClick = { onThemePreference(option) }, label = { Text(option.label) }, modifier = Modifier.heightIn(min = 48.dp).testTag("theme-${option.storageValue}").focusable())
                                }
                            }
                            Text("«Системная» следует настройке Android.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 10.dp))
                        }
                        "Проверка и помощь" -> {
                            PageHeading("Проверка и помощь", subtitle = "Соединение, журнал, поддержка")
                            QuietDivider()
                            QuietRow("Проверить соединение", subtitle = "Пошаговая проверка сети и сервера", onClick = { scope.launch { diagnostics = withContext(Dispatchers.IO) { ConnectionDiagnostics.run(context) } } }, modifier = Modifier.testTag("settings-diagnostics"))
                            QuietDivider()
                            QuietRow("Поделиться журналом", subtitle = "Ссылки подписок в журнале скрыты", onClick = { shareEventLog(context) }, modifier = Modifier.testTag("settings-share-log"))
                            QuietDivider()
                            QuietRow("Помощь и поддержка", subtitle = "Как получить подписку и подключиться", onClick = { openPage("Помощь") })
                            QuietDivider()
                            if (android.os.Build.MANUFACTURER.equals("samsung", true)) {
                                QuietRow(
                                    "Скрыть значок VPN на Samsung",
                                    onClick = {
                                        runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, android.net.Uri.parse("samsungapps://ProductDetail/com.samsung.android.qstuner"))) }
                                            .recoverCatching { context.startActivity(Intent(Intent.ACTION_VIEW, android.net.Uri.parse("https://apps.samsung.com/appquery/appDetail.as?appId=com.samsung.android.qstuner"))) }
                                            .onFailure { onNotice("Не удалось открыть Galaxy Store. Найдите QuickStar в приложении Good Lock.") }
                                    },
                                )
                                QuietDivider()
                            }
                            if (notice != null && !showAddSheet) Text(notice, color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 12.dp))
                        }
                        "Перенос настроек" -> {
                            PageHeading("Перенос настроек", subtitle = "Импорт и экспорт настроек и подписок")
                            QuietDivider()
                            QuietRow("Импорт из файла", subtitle = "Резервная копия, подписка или конфигурация", onClick = onImportFile, modifier = Modifier.testTag("transfer-import"))
                            QuietDivider()
                            QuietRow("Экспорт", subtitle = "Сохранить настройки и подписки в файл", onClick = { openPage("Экспорт") }, modifier = Modifier.testTag("transfer-export"))
                            QuietDivider()
                            if (notice != null && !showAddSheet) Text(notice, color = MaterialTheme.colorScheme.primary, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 12.dp))
                        }
                        "Дополнительные параметры" -> {
                            PageHeading("Дополнительные параметры", subtitle = "Редкие и технические настройки")
                            QuietDivider()
                            QuietRow(
                                "Блокировка рекламных сайтов", subtitle = "Блокирует рекламные домены. Реклама внутри видео, например в YouTube, может остаться.",
                                trailing = { Switch(checked = profile.blockYoutubeAds, onCheckedChange = { onSaveRouteSettings(profile.copy(blockYoutubeAds = it)) }, modifier = Modifier.testTag("home-block-youtube-ads").semantics { contentDescription = "Реклама YouTube" }) },
                            )
                            QuietDivider()
                            QuietRow(
                                "Напоминать о сроке", subtitle = "Уведомление перед окончанием подписки",
                                trailing = { Switch(checked = profile.expiryRemindersEnabled, onCheckedChange = onExpiryReminders, modifier = Modifier.semantics { contentDescription = "Напоминать о сроке" }) },
                            )
                            QuietDivider()
                            QuietRow("Пройти знакомство заново", subtitle = "Подписки и правила останутся без изменений", titleColor = MaterialTheme.colorScheme.error, onClick = { pageHistory.clear(); page = "Главная"; onRestartOnboarding() }, modifier = Modifier.testTag("restart-onboarding"))
                            QuietDivider()
                        }
                        "Помощь" -> {
                            PageHeading("Помощь")
                            Text("Подскажем, как получить ссылку и подключиться", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(bottom = 20.dp))
                            Surface(color = MaterialTheme.colorScheme.surface, shape = MaterialTheme.shapes.large) {
                                Column(Modifier.fillMaxWidth().padding(20.dp)) {
                                    Text("Нет подписки?", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
                                    Text("Откройте Telegram-бота партнёров — он поможет получить подписку.", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp, bottom = 16.dp))
                                    Button(onClick = onHelp, modifier = Modifier.fillMaxWidth().heightIn(min = 50.dp)) { Text("Открыть @CebCehVPN_bot") }
                                }
                            }
                            Text("Подписку можно добавить по ссылке, из буфера обмена, QR-кода или файла, а также через «Поделиться». Поддерживаются URI VLESS, VMess, SOCKS, Trojan, Shadowsocks, Hysteria2 и распространённые форматы sing-box, Xray и Clash.", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 18.dp))
                            Text("Если VPN не запускается: проверьте, что список узлов не пуст, разрешение VPN выдано, а срок подписки не истёк.", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 14.dp))
                        }
                        "Экспорт" -> {
                            PageHeading("Экспорт", subtitle = "Выберите, что перенести")
                            Text("Выберите, что перенести. Файлы могут содержать ссылки подписок или собственную конфигурацию с данными доступа.", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(bottom = 12.dp))
                            QuietDivider()
                            ProfileExportScope.entries.forEach { scope ->
                                QuietRow(
                                    scope.title,
                                    subtitle = when (scope) {
                                        ProfileExportScope.SETTINGS -> "Правила, приложения и конфигурация"
                                        ProfileExportScope.SUBSCRIPTIONS -> "Узлы и данные подписок"
                                        ProfileExportScope.ALL -> "Настройки и подписки вместе"
                                    },
                                    onClick = { onExport(scope) },
                                )
                                QuietDivider()
                            }
                        }
                    }
                    Spacer(Modifier.height(24.dp))
                }
            }
        }
        val banner = result
        if (banner != null && onboardingCompleted && !showAddSheet && page != "Вручную") {
            LaunchedEffect(banner.id) { kotlinx.coroutines.delay(if (banner.error) 12_000L else 6_000L); onDismissResult() }
            ResultBanner(banner.message, banner.error, onDismissResult, Modifier.align(Alignment.BottomCenter).safeDrawingPadding().padding(16.dp).testTag("result-banner"))
        }
      }
    }
    if (showAddSheet) {
        val importingNow by rememberUpdatedState(importing)
        val addSheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true, confirmValueChange = { !importingNow })
        val resultAtOpen = remember { result?.id }
        ModalBottomSheet(onDismissRequest = { if (!importing) showAddSheet = false }, sheetState = addSheetState, containerColor = MaterialTheme.colorScheme.background) {
            Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(horizontal = 24.dp).padding(bottom = 28.dp).imePadding().testTag("add-sheet")) {
                Text("Добавить подписку", style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.onSurface)
                Text("Подписка — это ссылка с серверами от вашего провайдера. CehoProxy сам её не выдаёт.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 6.dp))
                OutlinedTextField(value = importValue, onValueChange = { importValue = it }, label = { Text("Ссылка или конфигурация") }, enabled = !importing, minLines = 2, maxLines = 4, shape = MaterialTheme.shapes.large, modifier = Modifier.fillMaxWidth().padding(top = 16.dp).testTag("import-input"))
                QuietButton("Добавить подписку", { onTextImport(importValue) }, Modifier.fillMaxWidth().padding(top = 12.dp).testTag("import-submit"), enabled = !importing && importValue.isNotBlank())
                if (importing) {
                    LinearProgressIndicator(modifier = Modifier.fillMaxWidth().padding(top = 12.dp).testTag("import-progress"))
                    Text("Загружаем и проверяем узлы…", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp))
                } else result?.takeIf { it.error && it.id != resultAtOpen }?.let { Text(it.message, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(top = 12.dp).testTag("import-error")) }
                CaptionLabel("Другие способы")
                QuietDivider()
                QuietRow("Из буфера обмена", subtitle = "Ссылка или текст конфигурации", onClick = onImportClipboard, enabled = !importing, leading = { QuietBadge(size = 40.dp) { CopyGlyph(MaterialTheme.colorScheme.primary) } }, modifier = Modifier.testTag("add-sheet-paste"))
                QuietDivider()
                QuietRow("Камера: QR-код", subtitle = "Сканирование камерой телефона", onClick = onScanQr, enabled = !importing, leading = { QuietBadge(size = 40.dp) { ScanGlyph(MaterialTheme.colorScheme.primary) } }, modifier = Modifier.testTag("add-sheet-qr"))
                QuietDivider()
                QuietRow("QR-код из изображения", subtitle = "Выбрать картинку из галереи", onClick = onImportQrImage, enabled = !importing, leading = { QuietBadge(size = 40.dp) { ScanGlyph(MaterialTheme.colorScheme.primary) } }, modifier = Modifier.testTag("add-sheet-qr-image"))
                QuietDivider()
                QuietRow("Ввести вручную", subtitle = "Адрес, порт, логин и пароль", onClick = { openManualForm() }, enabled = !importing, leading = { QuietBadge(size = 40.dp) { SlidersGlyph(MaterialTheme.colorScheme.primary) } }, modifier = Modifier.testTag("add-sheet-manual"))
                QuietDivider()
                QuietRow("Файл", subtitle = "Конфигурация .json или .txt", onClick = onImportFile, enabled = !importing, leading = { QuietBadge(size = 40.dp) { FileGlyph(MaterialTheme.colorScheme.primary) } }, modifier = Modifier.testTag("add-sheet-file"))
                QuietDivider()
            }
        }
    }
    if (showSitesSheet) SitesSheet(
        onlySites = profile.onlySites,
        initial = profile.sites,
        onClose = { list ->
            showSitesSheet = false
            if (list != profile.sites) onSites(list.joinToString(", "))
        },
    )
    shareCandidate?.let { url ->
        val node = profile.nodes.firstOrNull { SubscriptionRemovalPolicy.urlFor(profile, it.subscriptionMetadata) == url }
        val title = node?.subscriptionMetadata?.title ?: "Подписка"
        val serverCount = profile.nodes.count { SubscriptionMembership.belongs(it, url, profile.subscriptionUrls) }
        ModalBottomSheet(onDismissRequest = { shareCandidate = null }, sheetState = shareSheetState, containerColor = MaterialTheme.colorScheme.surfaceContainer) {
            Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp).padding(bottom = 28.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Text("Поделиться подпиской", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.fillMaxWidth())
                Text("$title · $serverCount серверов", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.fillMaxWidth().padding(top = 4.dp))
                QrCodeImage(url, Modifier.padding(top = 16.dp).size(168.dp).testTag("subscription-qr"))
                Text("Отсканируйте в приложении, чтобы добавить подписку", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 10.dp))
                SheetOption(title = "Скопировать ссылку", subtitle = "Ссылка подписки в буфер обмена", glyph = { CopyGlyph(MaterialTheme.colorScheme.primary) }, modifier = Modifier.testTag("subscription-share-link"), onClick = { copySubscriptionLink(clipboardContext, url); onNotice("Ссылка подписки скопирована"); shareCandidate = null })
                SheetOption(title = "Отправить…", subtitle = "Системное меню «Поделиться»", glyph = { ShareGlyph(MaterialTheme.colorScheme.primary) }, modifier = Modifier.testTag("subscription-share-system"), onClick = { shareSubscriptionLink(clipboardContext, url, telegram = false); shareCandidate = null })
                SheetOption(title = "Отправить в Telegram", subtitle = "Откроется выбор чата", glyph = { TelegramGlyph(Color(0xFF2AABEE)) }, modifier = Modifier.testTag("subscription-share-telegram"), onClick = { shareSubscriptionLink(clipboardContext, url, telegram = true); shareCandidate = null })
            }
        }
    }
    diagnostics?.let { steps ->
        AlertDialog(onDismissRequest = { diagnostics = null }, title = { Text("Проверка соединения") }, text = {
            Column {
                steps.forEach { step ->
                    Text(step.title, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface)
                    Text(step.result, style = MaterialTheme.typography.bodyMedium, color = if (step.ok) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error, modifier = Modifier.padding(bottom = 12.dp))
                }
            }
        }, confirmButton = { TextButton(onClick = { diagnostics = null }) { Text("Закрыть") } })
    }
    if (showAppRoutePicker) {
        val excluded = if (profile.excludeSelectedApps) appsValue.split(',', '\n').map(String::trim).toSet() else emptySet()
        val candidates = appChoices.filter { it.packageName !in appNodeRoutes && it.packageName !in excluded && (it.label.contains(routeAppSearch, true) || it.packageName.contains(routeAppSearch, true)) }
        AlertDialog(
            onDismissRequest = { showAppRoutePicker = false },
            title = { Text("Приложение для отдельного выхода") },
            text = {
                Column {
                    OutlinedTextField(value = routeAppSearch, onValueChange = { routeAppSearch = it }, label = { Text("Поиск") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                    LazyColumn(Modifier.heightIn(max = 420.dp)) {
                        items(candidates, key = { it.packageName }) { app ->
                            Row(Modifier.fillMaxWidth().heightIn(min = 56.dp), verticalAlignment = Alignment.CenterVertically) {
                                androidx.compose.foundation.Image(app.icon.toBitmap(48, 48).asImageBitmap(), contentDescription = null, modifier = Modifier.size(36.dp).clip(RoundedCornerShape(9.dp)))
                                Column(Modifier.weight(1f).padding(start = 10.dp)) { Text(app.label, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis); Text(app.packageName, color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis) }
                                TextButton(onClick = { appNodeRoutes = appNodeRoutes + (app.packageName to ""); showAppRoutePicker = false }) { Text("Добавить") }
                            }
                        }
                    }
                }
            },
            confirmButton = { TextButton(onClick = { showAppRoutePicker = false }) { Text("Готово") } },
        )
    }

    if (showAppPicker) {
        AlertDialog(onDismissRequest = { showAppPicker = false }, title = { Text("Выберите приложения") }, text = {
            Column {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                    Text("Все приложения, кроме отмеченных", modifier = Modifier.weight(1f))
                    Switch(checked = profile.excludeSelectedApps, onCheckedChange = onExcludeSelectedApps)
                }
                Text(if (profile.excludeSelectedApps) "Отмеченные приложения останутся напрямую." else "Отмеченные приложения пойдут через VPN.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(bottom = 8.dp))
                if (!profile.excludeSelectedApps) {
                    TextButton(onClick = {
                        val starterApps = StarterAppPolicy.installed(appChoices.map { it.packageName })
                        selectedApps = selectedApps + starterApps
                        appSearch = ""
                        pickerNotice = if (starterApps.isEmpty()) "Стартовые приложения не найдены среди установленных." else "Добавлено: ${starterApps.size}. Всего отмечено: ${selectedApps.size}. Проверьте список и нажмите «Готово»."
                    }, modifier = Modifier.heightIn(min = 48.dp)) { Text("Добавить стартовый набор") }
                } else {
                    Text("Чтобы добавить стартовый набор, выключите переключатель выше.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(bottom = 8.dp))
                }
                pickerNotice?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(bottom = 8.dp)) }
                OutlinedTextField(value = appSearch, onValueChange = { appSearch = it }, label = { Text("Поиск") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                LazyColumn(Modifier.heightIn(max = 420.dp)) {
                    items(appChoices.filter { it.label.contains(appSearch, true) || it.packageName.contains(appSearch, true) }, key = { it.packageName }) { app ->
                        Row(Modifier.fillMaxWidth().clickable { selectedApps = if (app.packageName in selectedApps) selectedApps - app.packageName else selectedApps + app.packageName }.heightIn(min = 56.dp), verticalAlignment = Alignment.CenterVertically) {
                            androidx.compose.foundation.Image(app.icon.toBitmap(48, 48).asImageBitmap(), contentDescription = null, modifier = Modifier.size(38.dp).clip(RoundedCornerShape(9.dp)))
                            Column(Modifier.weight(1f).padding(start = 10.dp)) { Text(app.label, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis); Text(app.packageName, color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall, maxLines = 1, overflow = TextOverflow.Ellipsis) }
                            Checkbox(checked = app.packageName in selectedApps, onCheckedChange = { checked -> selectedApps = if (checked) selectedApps + app.packageName else selectedApps - app.packageName })
                        }
                    }
                }
            }
        }, confirmButton = { TextButton(onClick = { appsValue = selectedApps.sorted().joinToString(", "); onApps(appsValue); showAppPicker = false }) { Text("Готово") } }, dismissButton = { TextButton(onClick = { showAppPicker = false }) { Text("Отмена") } })
    }
}

@Composable
private fun FirstRunOnboarding(
    profile: ClientProfile,
    onPickCountry: (String) -> Unit,
    onImportFile: () -> Unit,
    onImportClipboard: () -> Unit,
    onScanQr: () -> Unit,
    onHelp: () -> Unit,
    onFinish: () -> Unit,
    notice: String?,
    modifier: Modifier = Modifier,
) {
    var step by rememberSaveable { mutableIntStateOf(0) }
    var showOther by remember { mutableStateOf(false) }
    val nodesOnEntry = remember(step) { profile.nodes.size }
    LaunchedEffect(step, profile.nodes.size) { if (step == 1 && profile.nodes.size > nodesOnEntry) step = 2 }
    BackHandler(enabled = step > 0) { step -= 1 }
    val colors = MaterialTheme.colorScheme
    Column(modifier.verticalScroll(rememberScrollState()).padding(horizontal = 24.dp, vertical = 8.dp).widthIn(max = 760.dp)) {
        if (step > 0) IconButton(onClick = { step -= 1 }, modifier = Modifier.size(48.dp).testTag("onboarding-back").semantics { contentDescription = "Назад" }) { BackGlyph(colors.onSurface) }
        when (step) {
            0 -> {
                VortexArt(Modifier.fillMaxWidth().padding(top = 8.dp).height(220.dp))
                Text("CEHOPROXY", style = MaterialTheme.typography.labelMedium.copy(letterSpacing = 2.sp), color = colors.onSurfaceVariant, modifier = Modifier.padding(top = 24.dp))
                Text("Интернет.\nВ вашем ритме.", style = cappedStyle(MaterialTheme.typography.displaySmall), color = colors.onSurface, modifier = Modifier.padding(top = 8.dp))
                Text("Добавьте подписку и выберите, какие приложения будут работать через VPN.", style = MaterialTheme.typography.bodyLarge, color = colors.onSurfaceVariant, modifier = Modifier.padding(top = 12.dp))
                QuietButton("Настроить подключение", { step = 1 }, Modifier.fillMaxWidth().padding(top = 28.dp).testTag("onboarding-next"), leading = { ArrowRightGlyph(colors.onPrimary) })
                TextButton(onClick = onFinish, modifier = Modifier.align(Alignment.CenterHorizontally).heightIn(min = 48.dp).testTag("onboarding-skip")) { Text("Уже настроено? Открыть главную", color = colors.primary) }
                Text("Подписку предоставляет ваш провайдер", style = MaterialTheme.typography.bodySmall, color = colors.onSurfaceVariant, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 8.dp))
            }
            1 -> {
                Text("Добавьте подписку", style = cappedStyle(MaterialTheme.typography.headlineMedium), color = colors.onSurface, modifier = Modifier.padding(top = 16.dp))
                Text("Подписка — это ссылка с серверами от вашего провайдера. CehoProxy сам подписки не выдаёт. Вставьте ссылку, остальное настроится само.", color = colors.onSurfaceVariant, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(top = 8.dp, bottom = 20.dp))
                if (profile.nodes.isNotEmpty()) {
                    Text("Подписка уже добавлена: серверов ${profile.nodes.size}.", color = colors.primary, style = MaterialTheme.typography.bodyLarge)
                    QuietButton("Далее", { step = 2 }, Modifier.fillMaxWidth().padding(top = 16.dp).testTag("onboarding-continue"))
                } else {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        QuietButton("Вставить из буфера", onImportClipboard, Modifier.weight(3f).testTag("onboarding-paste"))
                        Spacer(Modifier.width(8.dp))
                        Box(Modifier.weight(2f)) {
                            QuietButton("Другое", { showOther = true }, Modifier.fillMaxWidth().testTag("onboarding-other"), primary = false)
                            DropdownMenu(expanded = showOther, onDismissRequest = { showOther = false }) {
                                DropdownMenuItem(text = { Text("Сканировать QR-код") }, onClick = { showOther = false; onScanQr() })
                                DropdownMenuItem(text = { Text("Открыть файл") }, onClick = { showOther = false; onImportFile() })
                                DropdownMenuItem(text = { Text("Помощь в Telegram") }, onClick = { showOther = false; onHelp() })
                            }
                        }
                    }
                    notice?.let { Text(it, color = if (it.contains("ошиб", true) || it.contains("не удалось", true) || it.contains("не содержит", true) || it.contains("нет ", true)) colors.error else colors.primary, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 12.dp)) }
                    TextButton(onClick = { step = 2 }, modifier = Modifier.align(Alignment.CenterHorizontally).padding(top = 12.dp).heightIn(min = 48.dp).testTag("onboarding-later")) { Text("Добавить позже") }
                }
            }
            2 -> {
                Text("Какая у вас страна?", style = cappedStyle(MaterialTheme.typography.headlineMedium), color = colors.onSurface, modifier = Modifier.padding(top = 16.dp))
                Text("Ресурсы из набора выбранной страны открываются напрямую, без VPN. Это не значит, что напрямую пойдут все сайты страны. Если вашей страны нет в списке, выберите «Другая страна».", color = colors.onSurfaceVariant, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(top = 8.dp, bottom = 12.dp))
                QuietDivider()
                CountryRouting.countries.forEach { option ->
                    OnboardingCountryRow(
                        title = "${NodePresentation.flag(option.code.uppercase()) ?: ""} ${option.name}",
                        subtitle = option.coverage,
                        selected = profile.directCountryCode == option.code,
                        tag = "onboarding-country:${option.code}",
                        onClick = { onPickCountry(option.code) },
                    )
                    QuietDivider()
                }
                OnboardingCountryRow(title = "Другая страна", subtitle = "Все ресурсы пойдут по вашим правилам", selected = profile.directCountryCode.isEmpty(), tag = "onboarding-country:none", onClick = { onPickCountry("") })
                QuietDivider()
                if (profile.directCountryCode == "ru") Text("Для России блокировка рекламных сайтов включается сама. Отключить её можно в «Настройки → Дополнительные параметры».", color = colors.onSurfaceVariant, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 12.dp).testTag("onboarding-ads-note"))
                QuietButton("Далее", { step = 3 }, Modifier.fillMaxWidth().padding(top = 20.dp).testTag("onboarding-country-next"))
            }
            else -> {
                Text("Разрешение Android", style = cappedStyle(MaterialTheme.typography.headlineMedium), color = colors.onSurface, modifier = Modifier.padding(top = 16.dp))
                Text("При первом подключении Android покажет системный запрос на VPN-соединение. Без этого разрешения VPN не запустится. Если вы откажетесь, запрос можно повторить: нажмите на водоворот на главном экране.", color = colors.onSurfaceVariant, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(top = 8.dp, bottom = 20.dp))
                QuietButton("Перейти на главную", onFinish, Modifier.fillMaxWidth().testTag("onboarding-finish"))
            }
        }
    }
}

@Composable
private fun OnboardingCountryRow(title: String, subtitle: String?, selected: Boolean, tag: String, onClick: () -> Unit) {
    QuietRow(title, Modifier.testTag(tag), subtitle = subtitle, onClick = onClick, trailing = { RadioButton(selected = selected, onClick = null) }, selected = selected)
}

@Composable
private fun VortexArt(modifier: Modifier = Modifier) {
    val colors = MaterialTheme.colorScheme
    Canvas(modifier) {
        val center = Offset(size.width / 2f, size.height / 2f)
        val radius = minOf(size.width, size.height) * .46f
        drawCircle(colors.outlineVariant, radius * 1.02f, center, style = Stroke(1.dp.toPx()))
        drawCircle(colors.primaryContainer, radius * .14f, center)
        repeat(VORTEX_ARMS) { arm ->
            val spiral = Path().apply {
                for (step in 0..96) {
                    val point = vortexPoint(center, radius, step / 96f, arm)
                    if (step == 0) moveTo(point.x, point.y) else lineTo(point.x, point.y)
                }
            }
            drawPath(spiral, colors.secondary, style = Stroke(1.6.dp.toPx(), cap = StrokeCap.Round))
        }
    }
}

private fun countryOnboardingChoice(profile: ClientProfile, code: String): ClientProfile =
    profile.copy(directCountryCode = code, blockYoutubeAds = if (code == "ru") true else profile.blockYoutubeAds)

@Composable
private fun CountryRouteSelector(profile: ClientProfile, onCountry: (String) -> Unit, modifier: Modifier = Modifier) {
    var expanded by remember { mutableStateOf(false) }
    val selected = CountryRouting.find(profile.directCountryCode)
    val enabled = profile.importedConfig == null
    Column(modifier.fillMaxWidth()) {
        Text("Страна для прямых подключений", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
        Text("Ресурсы из набора выбранной страны идут напрямую внутри приложений, выбранных для VPN. Остальной трафик следует вашим правилам.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 4.dp, bottom = 8.dp))
        Box {
            OutlinedButton(onClick = { expanded = true }, enabled = enabled,
                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).testTag("country-selector")) {
                Text(selected?.name ?: "Не использовать")
            }
            DropdownMenu(expanded = expanded && enabled, onDismissRequest = { expanded = false }) {
                DropdownMenuItem(text = { Text("Не использовать") }, onClick = { expanded = false; onCountry("") }, modifier = Modifier.testTag("country-option:none"))
                CountryRouting.countries.forEach { country ->
                    DropdownMenuItem(text = {
                        Column {
                            Text(country.name)
                            Text(country.coverage, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }, onClick = { expanded = false; onCountry(country.code) }, modifier = Modifier.testTag("country-option:${country.code}"))
                }
            }
        }
        Text(if (!enabled) "Правила страны CehoProxy не применяются к импортированной конфигурации. Её маршруты сохраняются."
            else selected?.let { "${it.coverage}. Наборы правил сохранены в приложении и доступны без сети. Ручные правила сайтов важнее." }
                ?: "Выбор необязателен. Правила приложений и сайтов сохраняются.",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(top = 6.dp).testTag("country-coverage"))
    }
}

private fun cubicPoint(start: Offset, control1: Offset, control2: Offset, end: Offset, fraction: Float): Offset {
    val t = fraction.coerceIn(0f, 1f)
    val inverse = 1f - t
    return Offset(
        inverse * inverse * inverse * start.x + 3f * inverse * inverse * t * control1.x + 3f * inverse * t * t * control2.x + t * t * t * end.x,
        inverse * inverse * inverse * start.y + 3f * inverse * inverse * t * control1.y + 3f * inverse * t * t * control2.y + t * t * t * end.y,
    )
}

@Composable
internal fun TunnelVortex(
    apps: List<AppChoice>,
    active: Boolean,
    connected: Boolean,
    fullTunnel: Boolean,
    running: Boolean = connected,
    blocked: Boolean = false,
    directOnly: Boolean = false,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    var phase by remember { mutableFloatStateOf(0f) }
    // Counts finished passes; each pass shows the next batch of apps from the list.
    var batch by remember { mutableIntStateOf(0) }
    val animationsEnabled = systemAnimationsEnabled(LocalContext.current)
    val lifecycleState by androidx.lifecycle.compose.LocalLifecycleOwner.current.lifecycle.currentStateFlow.collectAsState()
    val motionEnabled = animationsEnabled && lifecycleState.isAtLeast(androidx.lifecycle.Lifecycle.State.RESUMED)
    LaunchedEffect(active, motionEnabled) {
        if (active && motionEnabled) {
            while (true) {
                val next = phase + .009f
                if (next >= 1f) batch++
                phase = next % 1f
                kotlinx.coroutines.delay(32)
            }
        } else {
            phase = 0f
            batch = 0
        }
    }
    val displayedApps = if (apps.isEmpty()) emptyList() else List(minOf(VORTEX_APP_LIMIT, apps.size)) { apps[(batch * VORTEX_APP_LIMIT + it) % apps.size] }
    val themeColors = MaterialTheme.colorScheme
    val wide = androidx.compose.ui.platform.LocalConfiguration.current.screenWidthDp >= 600
    Surface(
        onClick = onClick,
        modifier = modifier.height(220.dp).focusable().testTag("home-vortex").semantics {
            contentDescription = when {
                connected -> "VPN подключён. Нажмите, чтобы выключить."
                running -> "Идёт подключение. Нажмите, чтобы отменить."
                else -> "VPN выключен. Нажмите, чтобы подключить."
            }
        },
        color = Color.Transparent,
        shape = RoundedCornerShape(24.dp),
    ) {
        BoxWithConstraints(Modifier.fillMaxSize()) {
            val density = androidx.compose.ui.platform.LocalDensity.current
            val widthPx = with(density) { maxWidth.toPx() }
            val heightPx = with(density) { maxHeight.toPx() }
            val center = Offset(widthPx / 2f, heightPx * .5f)
            val radius = with(density) { 96.dp.toPx() }.coerceAtMost(widthPx * .34f).coerceAtMost(heightPx * .48f)
            val moving = active && motionEnabled
            val turn = if (moving) phase * 2f * Math.PI.toFloat() else 0f
            Canvas(Modifier.fillMaxSize()) {
                drawCircle(color = themeColors.outlineVariant, radius = radius * 1.02f, center = center, style = Stroke(width = 1.dp.toPx()))
                drawCircle(color = if (active) themeColors.primaryContainer else themeColors.surfaceContainerHigh, radius = radius * .14f, center = center)
                repeat(VORTEX_ARMS) { arm ->
                    val spiral = Path().apply {
                        val steps = 96
                        for (step in 0..steps) {
                            val point = vortexPoint(center, radius, step / steps.toFloat(), arm, turn)
                            if (step == 0) moveTo(point.x, point.y) else lineTo(point.x, point.y)
                        }
                    }
                    drawPath(spiral, themeColors.secondary, style = Stroke(width = 1.6.dp.toPx(), cap = StrokeCap.Round))
                }
            }
            displayedApps.forEachIndexed { index, app ->
                // Apps are split between the spiral arms; on each arm they flow from the outer end to the centre while moving.
                val arm = index % VORTEX_ARMS
                val perArm = (displayedApps.size + VORTEX_ARMS - 1) / VORTEX_ARMS
                val slot = index / VORTEX_ARMS
                val spacing = vortexLength(center, radius) / perArm
                val baseSize = with(density) { (spacing * .9f).toDp() }.coerceIn(16.dp, 42.dp)
                val t = (slot.toFloat() / perArm + if (moving) phase else 0f) % 1f
                val point = vortexPoint(center, radius, t, arm, turn)
                val x = point.x
                val y = point.y
                val iconAlpha = if (moving) (t / .06f).coerceAtMost(1f) * ((1f - t) / .12f).coerceAtMost(1f) else 1f
                val iconSize = baseSize * (if (moving) 1f - t * .6f else 1f)
                val icon = remember(app.packageName) { app.icon.toBitmap(96, 96).asImageBitmap() }
                Surface(
                    modifier = Modifier.offset { IntOffset((x - with(density) { (iconSize / 2).toPx() }).roundToInt(), (y - with(density) { (iconSize / 2).toPx() }).roundToInt()) }
                        .size(iconSize).alpha(iconAlpha).clip(RoundedCornerShape(13.dp)),
                    color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(13.dp),
                    border = androidx.compose.foundation.BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
                ) {
                    androidx.compose.foundation.Image(icon, contentDescription = app.label, modifier = Modifier.fillMaxSize().padding(iconSize * .12f))
                }
            }
        }
    }
}


private const val VORTEX_ARMS = 6
private const val VORTEX_APP_LIMIT = 5

// t = 0 is the outer end of an arm, t = 1 is its centre. Same curve as the drawn arms; rotation turns the whole spiral.
private fun vortexPoint(center: Offset, radius: Float, t: Float, arm: Int = 0, rotation: Float = 0f): Offset {
    val angle = t * Math.PI.toFloat() * 4f + arm * 2f * Math.PI.toFloat() / VORTEX_ARMS + rotation
    val r = radius * (.88f - .70f * t)
    return Offset(center.x + kotlin.math.cos(angle) * r, center.y + kotlin.math.sin(angle) * r)
}

private fun vortexLength(center: Offset, radius: Float): Float {
    var length = 0f
    var previous = vortexPoint(center, radius, 0f)
    for (step in 1..96) {
        val point = vortexPoint(center, radius, step / 96f)
        length += (point - previous).getDistance()
        previous = point
    }
    return length
}

private fun systemAnimationsEnabled(context: Context): Boolean = runCatching {
    android.provider.Settings.Global.getFloat(
        context.contentResolver,
        android.provider.Settings.Global.ANIMATOR_DURATION_SCALE,
        1f,
    ) > 0f
}.getOrDefault(true)

@Composable
private fun RouteAssignmentRow(title: String, nodes: List<ProxyNode>, selectedRaw: String?, icon: Drawable? = null, onSelect: (String?) -> Unit) {
    var menuExpanded by remember { mutableStateOf(false) }
    val selectedName = nodes.firstOrNull { it.routeId == selectedRaw }?.name ?: "Общий узел"
    Row(
        Modifier.fillMaxWidth().padding(vertical = 4.dp).heightIn(min = 52.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon != null) androidx.compose.foundation.Image(icon.toBitmap(48, 48).asImageBitmap(), contentDescription = null, modifier = Modifier.size(34.dp).clip(RoundedCornerShape(8.dp)))
        Text(title, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
        Box {
            OutlinedButton(onClick = { menuExpanded = true }, modifier = Modifier.heightIn(min = 48.dp).testTag("route-select:$title")) {
                Text(selectedName, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            DropdownMenu(expanded = menuExpanded, onDismissRequest = { menuExpanded = false }) {
                DropdownMenuItem(text = { Text("Общий узел") }, onClick = { onSelect(null); menuExpanded = false })
                nodes.forEach { node ->
                    DropdownMenuItem(
                        text = { Text(NodePresentation.displayName(node.name), maxLines = 1, overflow = TextOverflow.Ellipsis) },
                        onClick = { onSelect(node.routeId); menuExpanded = false },
                    )
                }
            }
        }
    }
}

@Composable internal fun NodeCountryMark(country: VerifiedNodeCountry?, nameCode: String?) {
    val color = MaterialTheme.colorScheme.onSurfaceVariant
    val verified = country?.takeIf { NodeCountryCachePolicy.isFresh(it, System.currentTimeMillis()) }
    val code = nameCode ?: verified?.countryCode
    val flag = NodePresentation.flag(code)
    if (flag != null) Text(flag, fontSize = 24.sp, modifier = Modifier.size(32.dp).semantics { contentDescription = "Страна узла: $code" })
    else Canvas(Modifier.size(28.dp).semantics { contentDescription = "Страна узла пока не определена" }) {
        val stroke = Stroke(width = 1.6.dp.toPx())
        drawCircle(color = color, radius = size.minDimension * .42f, style = stroke)
        drawOval(color = color, topLeft = Offset(size.width * .28f, size.height * .08f), size = androidx.compose.ui.geometry.Size(size.width * .44f, size.height * .84f), style = stroke)
        drawLine(color = color, start = Offset(size.width * .08f, size.height * .5f), end = Offset(size.width * .92f, size.height * .5f), strokeWidth = stroke.width)
    }
}

@Composable private fun NodeRow(node: ProxyNode, selected: Boolean, onClick: () -> Unit, measurement: NodeMeasurement? = null, onCheck: (() -> Unit)? = null, modifier: Modifier = Modifier, autoEnabled: Boolean? = null, onAutoToggle: ((Boolean) -> Unit)? = null, autoSwitchTag: String = "", excludedLabel: String? = null, onRemove: (() -> Unit)? = null, removeTag: String = "") {
    val colors = MaterialTheme.colorScheme
    val subColor = if (selected) colors.onPrimaryContainer else colors.onSurfaceVariant
    Surface(
        onClick = onClick,
        modifier = modifier.fillMaxWidth().padding(vertical = 3.dp).heightIn(min = 64.dp).alpha(if (excludedLabel != null) .5f else 1f),
        shape = RoundedCornerShape(16.dp),
        color = if (selected) colors.primaryContainer else Color.Transparent,
    ) {
        Row(Modifier.padding(horizontal = 12.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
            QuietBadge(size = 44.dp, background = if (selected) colors.surface else colors.primaryContainer) { NodeCountryMark(measurement?.verifiedCountry, NodeCountry.resolve(node.name)) }
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Text(NodePresentation.withoutFlag(NodePresentation.displayName(node.name)), color = colors.onSurface, style = MaterialTheme.typography.bodyLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(node.type.uppercase() + " · " + NodeMetricsPolicy.summary(measurement), color = subColor, style = MaterialTheme.typography.bodySmall)
                excludedLabel?.let { Text(it, color = colors.error, style = MaterialTheme.typography.labelSmall) }
                if (onCheck != null || onRemove != null) Row {
                    if (onCheck != null) TextButton(onClick = onCheck, contentPadding = PaddingValues(end = 12.dp), modifier = Modifier.heightIn(min = 48.dp)) { Text(if (measurement?.testing == true) "Отмена" else "Скорость") }
                    if (onRemove != null) TextButton(onClick = onRemove, contentPadding = PaddingValues(horizontal = 12.dp), modifier = Modifier.heightIn(min = 48.dp).let { if (removeTag.isNotEmpty()) it.testTag(removeTag) else it }) { Text("Удалить", color = colors.error) }
                }
            }
            if (onAutoToggle != null) Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("Авто", style = MaterialTheme.typography.labelSmall, color = subColor)
                Switch(checked = autoEnabled == true, onCheckedChange = onAutoToggle, modifier = if (autoSwitchTag.isNotEmpty()) Modifier.testTag(autoSwitchTag) else Modifier)
            }
            if (onCheck == null && selected) Text("Выбран", color = colors.onPrimaryContainer, style = MaterialTheme.typography.labelMedium, modifier = Modifier.padding(start = 8.dp))
        }
    }
}

@Composable private fun SubscriptionCard(metadata: SubscriptionMetadata, expiresAt: Long?, fetchedAt: Long?, serverCount: Int, onOpenLink: (String) -> Unit, onShare: (() -> Unit)?, onSettings: (() -> Unit)?, modifier: Modifier = Modifier) {
    val telegramBlue = Color(0xFF2AABEE)
    Column(modifier.fillMaxWidth()) {
        Text("ПОДПИСКА", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.primary)
        Row(Modifier.fillMaxWidth().padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(metadata.title ?: metadata.sourceHost ?: "Подписка", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            telegramLink(metadata)?.let { link ->
                IconButton(onClick = { onOpenLink(link) }, modifier = Modifier.size(34.dp).testTag("home-subscription-telegram").semantics { contentDescription = "Telegram подписки" }) { TelegramGlyph(telegramBlue) }
            }
            onShare?.let { share -> IconButton(onClick = share, modifier = Modifier.size(34.dp).testTag("home-subscription-share").semantics { contentDescription = "Поделиться подпиской" }) { ShareGlyph(MaterialTheme.colorScheme.primary) } }
            onSettings?.let { open -> IconButton(onClick = open, modifier = Modifier.size(34.dp).testTag("home-subscription-settings").semantics { contentDescription = "Настройки подписки" }) { MoreGlyph(MaterialTheme.colorScheme.onSurfaceVariant) } }
        }
        val updated = fetchedAt?.let { SubscriptionRefreshPolicy.fetchedLabel(it, System.currentTimeMillis()) }
        Text(listOfNotNull("$serverCount серверов", updated).joinToString(" · "), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 2.dp))
        if (expiresAt != null || metadata.usedBytes != null) SubscriptionBars(metadata, expiresAt, Modifier.padding(top = 14.dp))
    }
}

@Composable private fun SubscriptionBars(metadata: SubscriptionMetadata, expiresAt: Long?, modifier: Modifier = Modifier) {
    val nowSeconds = System.currentTimeMillis() / 1000
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        expiresAt?.takeIf { it > 0 }?.let { expires ->
            val days = ((expires - nowSeconds) / 86_400L).coerceAtLeast(0L)
            SubscriptionBar(title = "Срок", value = "$days дн. · до ${subscriptionDateLabel(expires)}", progress = (days / 365f).coerceIn(0f, 1f), dashed = false)
        }
        metadata.usedBytes?.let { used ->
            val total = metadata.totalBytes?.takeIf { it > 0 }
            SubscriptionBar(
                title = "Трафик",
                value = if (total != null) "${formatBytes(used)} из ${formatBytes(total)}" else "${formatBytes(used)} · без лимита",
                progress = total?.let { (used.toFloat() / it).coerceIn(0f, 1f) } ?: 1f,
                dashed = total == null,
            )
        }
    }
}

@Composable private fun SubscriptionBar(title: String, value: String, progress: Float, dashed: Boolean) {
    val track = MaterialTheme.colorScheme.surfaceContainerHighest
    val fill = MaterialTheme.colorScheme.primary
    Column {
        Row {
            Text(title, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
            Text(value, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Canvas(Modifier.fillMaxWidth().padding(top = 6.dp).height(8.dp)) {
            drawRoundRect(color = track, cornerRadius = CornerRadius(size.height / 2f))
            if (dashed) drawLine(color = fill, start = Offset(0f, size.height / 2f), end = Offset(size.width, size.height / 2f), strokeWidth = size.height, pathEffect = PathEffect.dashPathEffect(floatArrayOf(14f, 10f)))
            else drawRoundRect(color = fill, size = Size(size.width * progress, size.height), cornerRadius = CornerRadius(size.height / 2f))
        }
    }
}

@Composable private fun SubscriptionListItem(index: Int, metadata: SubscriptionMetadata, serverCount: Int, fetchedAt: Long?, expiresAt: Long?, refreshing: Boolean, detailsShown: Boolean, onRefresh: () -> Unit, onCopy: () -> Unit, onDetails: () -> Unit, onShare: () -> Unit, onRemove: () -> Unit, onOpenLink: (String) -> Unit, modifier: Modifier = Modifier) {
    Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(18.dp), modifier = modifier.fillMaxWidth().testTag("subscription-item:$index")) {
        Column(Modifier.padding(18.dp)) {
            SubscriptionCard(metadata = metadata, expiresAt = expiresAt, fetchedAt = fetchedAt, serverCount = serverCount, onOpenLink = onOpenLink, onShare = null, onSettings = null)
            Row(Modifier.fillMaxWidth().padding(top = 14.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                PillButton("Обновить", onRefresh, Modifier.weight(1f).testTag("subscription-refresh:$index"), enabled = !refreshing)
                PillButton("Копировать", onCopy, Modifier.weight(1f).testTag("subscription-copy:$index"))
                PillButton(if (detailsShown) "Скрыть" else "Сведения", onDetails, Modifier.weight(1f).testTag("subscription-details-toggle:$index"))
            }
            Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                PillButton("Поделиться", onShare, Modifier.weight(1f).testTag("subscription-share:$index"))
                PillButton("Удалить", onRemove, Modifier.weight(1f).testTag("subscription-remove:$index"), textColor = MaterialTheme.colorScheme.error)
            }
            if (detailsShown) SubscriptionDetails(metadata, onOpenLink)
        }
    }
}

@Composable private fun PillButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, textColor: Color = MaterialTheme.colorScheme.onSurface) {
    Surface(
        onClick = onClick,
        enabled = enabled,
        shape = RoundedCornerShape(14.dp),
        color = MaterialTheme.colorScheme.surfaceContainerHigh,
        modifier = modifier.heightIn(min = 48.dp),
    ) {
        Box(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 10.dp), contentAlignment = Alignment.Center) {
            Text(text, style = MaterialTheme.typography.labelLarge, color = if (enabled) textColor else MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable private fun SectionLabel(text: String) {
    Text(text, style = MaterialTheme.typography.labelMedium.copy(letterSpacing = 1.4.sp), color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.fillMaxWidth().padding(start = 4.dp, top = 24.dp, bottom = 8.dp))
}

@Composable private fun SettingsGroup(content: @Composable ColumnScope.() -> Unit) {
    Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(18.dp), modifier = Modifier.fillMaxWidth()) {
        Column(content = content)
    }
}

@Composable private fun SettingsRow(title: String, modifier: Modifier = Modifier, value: String? = null, onClick: (() -> Unit)? = null, switchChecked: Boolean? = null, onSwitch: ((Boolean) -> Unit)? = null, color: Color = MaterialTheme.colorScheme.onSurface) {
    Row(
        modifier.fillMaxWidth().heightIn(min = 54.dp).let { if (onClick != null) it.clickable(onClick = onClick) else it }.padding(horizontal = 16.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(title, color = color, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f))
        value?.let { Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.padding(start = 8.dp)) }
        switchChecked?.let { Switch(checked = it, onCheckedChange = onSwitch ?: {}, modifier = Modifier.padding(start = 8.dp).semantics { contentDescription = title }) }
        if (onClick != null && switchChecked == null) { Spacer(Modifier.width(8.dp)); ChevronGlyph(MaterialTheme.colorScheme.onSurfaceVariant) }
    }
}

@Composable private fun SheetOption(title: String, subtitle: String, glyph: @Composable () -> Unit, onClick: () -> Unit, modifier: Modifier = Modifier) {
    Surface(onClick = onClick, shape = RoundedCornerShape(18.dp), color = MaterialTheme.colorScheme.surfaceContainerHigh, modifier = modifier.fillMaxWidth().padding(top = 8.dp)) {
        Row(Modifier.padding(horizontal = 14.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(40.dp).clip(RoundedCornerShape(13.dp)).background(MaterialTheme.colorScheme.primaryContainer), contentAlignment = Alignment.Center) { glyph() }
            Column(Modifier.weight(1f).padding(start = 14.dp)) {
                Text(title, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface)
                Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 2.dp))
            }
        }
    }
}

@Composable private fun BackGlyph(color: Color) {
    Canvas(Modifier.size(22.dp)) {
        drawPath(Path().apply { moveTo(size.width * .62f, size.height * .2f); lineTo(size.width * .32f, size.height * .5f); lineTo(size.width * .62f, size.height * .8f) }, color, style = Stroke(2.dp.toPx(), cap = StrokeCap.Round))
    }
}

@Composable private fun LinkGlyph(color: Color) {
    Canvas(Modifier.size(20.dp)) {
        val stroke = Stroke(1.8.dp.toPx(), cap = StrokeCap.Round)
        rotate(-45f) {
            drawRoundRect(color, topLeft = Offset(size.width * .06f, size.height * .34f), size = Size(size.width * .44f, size.height * .32f), cornerRadius = CornerRadius(size.height * .16f), style = stroke)
            drawRoundRect(color, topLeft = Offset(size.width * .50f, size.height * .34f), size = Size(size.width * .44f, size.height * .32f), cornerRadius = CornerRadius(size.height * .16f), style = stroke)
        }
    }
}

@Composable private fun ScanGlyph(color: Color) {
    Canvas(Modifier.size(20.dp)) {
        val w = size.width
        val h = size.height
        val path = Path().apply {
            moveTo(w * .12f, h * .32f); lineTo(w * .12f, h * .12f); lineTo(w * .32f, h * .12f)
            moveTo(w * .68f, h * .12f); lineTo(w * .88f, h * .12f); lineTo(w * .88f, h * .32f)
            moveTo(w * .88f, h * .68f); lineTo(w * .88f, h * .88f); lineTo(w * .68f, h * .88f)
            moveTo(w * .32f, h * .88f); lineTo(w * .12f, h * .88f); lineTo(w * .12f, h * .68f)
        }
        val stroke = Stroke(1.8.dp.toPx(), cap = StrokeCap.Round)
        drawPath(path, color, style = stroke)
        drawLine(color, Offset(w * .2f, h * .5f), Offset(w * .8f, h * .5f), strokeWidth = stroke.width, cap = StrokeCap.Round)
    }
}

@Composable private fun FileGlyph(color: Color) {
    Canvas(Modifier.size(20.dp)) {
        val w = size.width
        val h = size.height
        val path = Path().apply { moveTo(w * .22f, h * .1f); lineTo(w * .6f, h * .1f); lineTo(w * .8f, h * .3f); lineTo(w * .8f, h * .9f); lineTo(w * .22f, h * .9f); close() }
        drawPath(path, color, style = Stroke(1.8.dp.toPx(), cap = StrokeCap.Round))
    }
}

@Composable private fun CopyGlyph(color: Color) {
    Canvas(Modifier.size(20.dp)) {
        val stroke = Stroke(1.8.dp.toPx(), cap = StrokeCap.Round)
        drawRoundRect(color, topLeft = Offset(size.width * .14f, size.height * .14f), size = Size(size.width * .56f, size.height * .56f), cornerRadius = CornerRadius(size.width * .1f), style = stroke)
        drawRoundRect(color, topLeft = Offset(size.width * .3f, size.height * .3f), size = Size(size.width * .56f, size.height * .56f), cornerRadius = CornerRadius(size.width * .1f), style = stroke)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable private fun SitesSheet(onlySites: Boolean, initial: List<String>, onClose: (List<String>) -> Unit) {
    var sites by remember { mutableStateOf(initial) }
    var input by remember { mutableStateOf("") }
    var rejected by remember { mutableStateOf<List<String>>(emptyList()) }
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)
    fun addInput() {
        val parsed = SiteListInput.parse(input)
        sites = sites + parsed.hosts.filter { it !in sites }
        rejected = parsed.rejected
        input = parsed.rejected.joinToString(", ")
    }
    ModalBottomSheet(onDismissRequest = { onClose(sites) }, sheetState = sheetState, containerColor = MaterialTheme.colorScheme.surfaceContainer) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp).padding(bottom = 24.dp)) {
            Text(if (onlySites) "Сайты через VPN" else "Сайты напрямую", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
            Text(
                if (onlySites) "Эти сайты идут через VPN, остальные — напрямую." else "Эти сайты идут напрямую, остальные — через VPN.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp, bottom = 14.dp),
            )
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = input,
                    onValueChange = { input = it; rejected = emptyList() },
                    singleLine = true,
                    placeholder = { Text("example.com или несколько через запятую") },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { addInput() }),
                    modifier = Modifier.weight(1f).testTag("sites-input"),
                )
                IconButton(onClick = { addInput() }, modifier = Modifier.padding(start = 8.dp).size(52.dp).testTag("sites-add").semantics { contentDescription = "Добавить сайт" }) { PlusGlyph(MaterialTheme.colorScheme.primary) }
            }
            if (rejected.isNotEmpty()) Text("Не похоже на домен: ${rejected.joinToString()}", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(top = 6.dp))
            Text("Сайтов: ${sites.size}", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 18.dp, bottom = 8.dp))
            if (sites.isEmpty()) Text("Список пуст. Добавьте сайт выше.", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            else Column(Modifier.fillMaxWidth().heightIn(max = 360.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                sites.forEach { site ->
                    Surface(color = MaterialTheme.colorScheme.surfaceContainerHigh, shape = RoundedCornerShape(16.dp), modifier = Modifier.fillMaxWidth().testTag("site-item:$site")) {
                        Row(Modifier.padding(start = 16.dp, end = 4.dp, top = 2.dp, bottom = 2.dp), verticalAlignment = Alignment.CenterVertically) {
                            Text(site, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                            IconButton(onClick = { sites = sites - site }, modifier = Modifier.testTag("site-remove:$site").semantics { contentDescription = "Удалить $site" }) { CloseGlyph(MaterialTheme.colorScheme.onSurfaceVariant) }
                        }
                    }
                }
            }
            Button(
                onClick = {
                    if (input.isNotBlank()) {
                        addInput()
                        if (rejected.isNotEmpty()) return@Button
                    }
                    onClose(sites)
                },
                modifier = Modifier.fillMaxWidth().padding(top = 18.dp).heightIn(min = 52.dp).testTag("sites-done"),
            ) { Text("Готово") }
        }
    }
}

@Composable private fun CloseGlyph(color: Color) {
    Canvas(Modifier.size(20.dp)) {
        val stroke = Stroke(1.8.dp.toPx(), cap = StrokeCap.Round)
        drawLine(color, Offset(size.width * .22f, size.height * .22f), Offset(size.width * .78f, size.height * .78f), strokeWidth = stroke.width, cap = StrokeCap.Round)
        drawLine(color, Offset(size.width * .78f, size.height * .22f), Offset(size.width * .22f, size.height * .78f), strokeWidth = stroke.width, cap = StrokeCap.Round)
    }
}

internal fun subscriptionDateLabel(epochSeconds: Long): String =
    java.time.Instant.ofEpochSecond(epochSeconds).atZone(java.time.ZoneId.systemDefault()).format(java.time.format.DateTimeFormatter.ofPattern("dd.MM.yyyy"))

private fun shareSubscriptionLink(context: Context, url: String, telegram: Boolean) {
    val send = Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, url)
    if (telegram && runCatching { context.startActivity(Intent(send).setPackage("org.telegram.messenger")) }.isSuccess) return
    context.startActivity(Intent.createChooser(send, "Поделиться подпиской"))
}

private fun copySubscriptionLink(context: android.content.Context, url: String) {
    context.getSystemService(ClipboardManager::class.java).setPrimaryClip(android.content.ClipData.newPlainText("Подписка CehoProxy", url))
}

private fun telegramLink(metadata: SubscriptionMetadata): String? {
    val link = metadata.supportUrl?.takeIf { it.contains("t.me/", true) || it.startsWith("tg:") }
        ?: metadata.sourceHost?.takeIf { it.equals("cebceh.com", true) || it.endsWith(".cebceh.com", true) }?.let { "tg://resolve?domain=CebCehVPN_bot" }
        ?: return null
    return Regex("""^https?://t\.me/([A-Za-z0-9_]+)/?$""").find(link)?.let { "tg://resolve?domain=${it.groupValues[1]}" } ?: link
}

@Composable internal fun RefreshGlyph(color: Color, spinning: Boolean = false) {
    val transition = rememberInfiniteTransition(label = "refresh")
    val angle by transition.animateFloat(initialValue = 0f, targetValue = 360f, animationSpec = infiniteRepeatable(animation = tween(durationMillis = 900, easing = LinearEasing)), label = "refresh-angle")
    Canvas(Modifier.size(24.dp).graphicsLayer { rotationZ = if (spinning) angle else 0f }) {
        val path = addPathNodes("M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z").toPath()
        scale(size.minDimension / 24f, size.minDimension / 24f, pivot = Offset.Zero) { drawPath(path, color) }
    }
}

@Composable private fun QrCodeImage(text: String, modifier: Modifier = Modifier) {
    val bitmap = remember(text) {
        val modules = SubscriptionQr.modules(text)
        val n = modules.size
        val pixels = IntArray(n * n) { i -> if (modules[i / n][i % n]) android.graphics.Color.BLACK else android.graphics.Color.WHITE }
        android.graphics.Bitmap.createBitmap(pixels, n, n, android.graphics.Bitmap.Config.ARGB_8888).asImageBitmap()
    }
    androidx.compose.foundation.Image(bitmap, contentDescription = "QR-код подписки", filterQuality = FilterQuality.None, modifier = modifier.aspectRatio(1f))
}

@Composable private fun TelegramGlyph(color: Color) {
    Canvas(Modifier.size(24.dp)) {
        val s = size.minDimension / 24f
        val plane = Path().apply {
            moveTo(2f * s, 11.2f * s); lineTo(21f * s, 3.2f * s); lineTo(17.6f * s, 20.4f * s); lineTo(12.2f * s, 16.2f * s)
            lineTo(9.2f * s, 19.2f * s); lineTo(9.2f * s, 14.8f * s); lineTo(17.4f * s, 7.4f * s); lineTo(7.4f * s, 13.8f * s); close()
        }
        drawPath(plane, color)
    }
}

@Composable internal fun PlusGlyph(color: Color) {
    Canvas(Modifier.size(24.dp)) {
        val s = size.minDimension / 24f
        drawLine(color, Offset(12f * s, 5f * s), Offset(12f * s, 19f * s), strokeWidth = 2f * s, cap = StrokeCap.Round)
        drawLine(color, Offset(5f * s, 12f * s), Offset(19f * s, 12f * s), strokeWidth = 2f * s, cap = StrokeCap.Round)
    }
}

@Composable internal fun GearGlyph(color: Color) {
    Canvas(Modifier.size(24.dp)) {
        val s = size.minDimension / 24f
        val center = Offset(12f * s, 12f * s)
        drawCircle(color, radius = 3f * s, center = center, style = Stroke(width = 1.8f * s))
        drawCircle(color, radius = 7.5f * s, center = center, style = Stroke(width = 1.8f * s))
        repeat(8) { i ->
            val angle = i * Math.PI / 4
            val inner = Offset(center.x + (kotlin.math.cos(angle) * 7.5f * s).toFloat(), center.y + (kotlin.math.sin(angle) * 7.5f * s).toFloat())
            val outer = Offset(center.x + (kotlin.math.cos(angle) * 10.5f * s).toFloat(), center.y + (kotlin.math.sin(angle) * 10.5f * s).toFloat())
            drawLine(color, inner, outer, strokeWidth = 2.4f * s, cap = StrokeCap.Round)
        }
    }
}

@Composable private fun ShareGlyph(color: Color) {
    Canvas(Modifier.size(24.dp)) {
        val s = size.minDimension / 24f
        val a = Offset(18f * s, 5f * s)
        val b = Offset(6f * s, 12f * s)
        val c = Offset(18f * s, 19f * s)
        drawLine(color, b, a, strokeWidth = 1.8f * s)
        drawLine(color, b, c, strokeWidth = 1.8f * s)
        listOf(a, b, c).forEach { drawCircle(color, radius = 2.8f * s, center = it, style = Stroke(width = 1.8f * s)) }
    }
}

@Composable private fun MoreGlyph(color: Color) {
    Canvas(Modifier.size(24.dp)) {
        val s = size.minDimension / 24f
        listOf(5f, 12f, 19f).forEach { x -> drawCircle(color, radius = 1.9f * s, center = Offset(x * s, 12f * s)) }
    }
}

@Composable private fun SubscriptionDetails(metadata: SubscriptionMetadata, onOpenLink: (String) -> Unit) {
    if (metadata.usedBytes == null && metadata.totalBytes == null && metadata.title == null && metadata.infoText == null && metadata.announcement == null && metadata.supportUrl == null && metadata.webPageUrl == null && metadata.infoButtonUrl == null && metadata.renewUrl == null && !metadata.notifyOnExpiry) return
    Column(Modifier.fillMaxWidth().padding(top = 10.dp, bottom = 10.dp)) {
        metadata.title?.let { Text(it, color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.titleMedium) }
        val used = metadata.usedBytes
        val total = metadata.totalBytes?.takeIf { it > 0 }
        if (used != null || total != null) Text("Трафик: ${used?.let(::formatBytes) ?: "—"} / ${total?.let(::formatBytes) ?: "без лимита"}", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
        metadata.announcement?.let { Text(it, color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 6.dp)) }
        metadata.infoText?.let { Text(it, color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 6.dp)) }
        if (metadata.notifyOnExpiry) Text("Уведомления от провайдера: включены", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
        metadata.supportUrl?.let { TextButton(onClick = { onOpenLink(it) }) { Text(if (it.contains("t.me/", true) || it.startsWith("tg:")) "Telegram поддержки" else "Поддержка", color = MaterialTheme.colorScheme.primary) } }
        metadata.webPageUrl?.let { TextButton(onClick = { onOpenLink(it) }) { Text("Сайт подписки", color = MaterialTheme.colorScheme.primary) } }
        if (metadata.infoButtonUrl != null) TextButton(onClick = { onOpenLink(metadata.infoButtonUrl) }) { Text(metadata.infoButtonText ?: "Подробнее", color = MaterialTheme.colorScheme.primary) }
        metadata.renewUrl?.let { TextButton(onClick = { onOpenLink(it) }) { Text("Продлить подписку", color = MaterialTheme.colorScheme.primary) } }
    }
}

internal fun formatBytes(bytes: Long): String {
    if (bytes < 1024) return "$bytes Б"
    val units = listOf("КиБ", "МиБ", "ГиБ", "ТиБ")
    var value = bytes.toDouble()
    var unit = -1
    while (value >= 1024 && unit < units.lastIndex) { value /= 1024; unit++ }
    return String.format(Locale.getDefault(), "%.1f %s", value, units[unit])
}

@Composable private fun EmptyInstruction(title: String, body: String) {
    Column(Modifier.fillMaxWidth().padding(vertical = 24.dp), horizontalAlignment = Alignment.CenterHorizontally) { Text(title, color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.titleMedium); Text(body, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 6.dp)) }
}
