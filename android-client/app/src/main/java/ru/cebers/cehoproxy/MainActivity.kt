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
        if (measurements.connected) { metrics.checkLatency(routeIds); return }
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
                    .onSuccess { importText(it) }.onFailure { notice = "Не удалось прочитать файл: ${it.message}" }
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
            if (result.resultCode == Activity.RESULT_OK) connect() else notice = "Разрешение Android VPN не выдано"
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
                    profile = profile, connected = connected, connectedSince = connectedSince, serviceRunning = serviceRunning, blocked = blocked, measurements = measurements, notificationsBlocked = notificationsBlocked, onOpenNotificationSettings = ::openNotificationSettings, onCheckNode = metrics::check, onCheckNodes = ::checkNodes, onCancelNodeCheck = metrics::cancelProbe, notice = notice, importing = importing, importRevision = importRevision,
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
                        scanQr.startScan().addOnSuccessListener { code -> code.rawValue?.let(::importText) ?: run { notice = "В QR-коде нет текста" } }
                            .addOnFailureListener { notice = "Сканер недоступен. Выберите QR-изображение или вставьте ссылку из буфера обмена." }
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
                                if (fetched.isEmpty()) notice = "Подписка не ответила, узлы сохранены"
                                else {
                                    update(SubscriptionRefreshPolicy.applyFetchedSources(profile, fetched, System.currentTimeMillis()))
                                    notice = "Подписка обновлена: узлов ${fetched.values.sumOf { it.size }}"
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
                                if (fetched.isEmpty()) notice = "Подписки не ответили, узлы сохранены"
                                else {
                                    update(SubscriptionRefreshPolicy.applyFetchedSources(profile, fetched, System.currentTimeMillis()))
                                    notice = "Обновлено подписок: ${fetched.size} из ${urls.size}, узлов ${fetched.values.sumOf { it.size }}"
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
        if (text.isNullOrBlank()) notice = "В буфере обмена нет текста для импорта" else importText(text)
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
        if (::store.isInitialized) profile = ActiveNodeSelection.sync(profile, store.read())
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
        lifecycleScope.launch {
            try {
                val (nodes, rejectedByEngine, unsupportedTypes) = withContext(Dispatchers.IO) {
                    var metadata = SubscriptionMetadata()
                    val text =
                    if (kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION) {
                        SubscriptionTunnelFetch.fetch(applicationContext, input, requestOptions).also { metadata = it.metadata }.body
                    } else input
                    val sourceNodes = NodeShareParser.parse(text).let { if (kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION) SubscriptionNames.resolve(input, it) else it }
                    val (parsedNodes, rejected) = validateNodes(sourceNodes)
                    val fetchedMetadata = if (kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION) metadata else SubscriptionMetadata.fromBody(text)
                    val validNodes = fetchedMetadata.attachTo(parsedNodes)
                    Triple(validNodes, rejected, NodeShareParser.unsupportedTypes(text))
                }
                if (nodes.isEmpty()) {
                    notice = when {
                        input.trimStart().startsWith("happ://crypt", ignoreCase = true) -> "Зашифрованную подписку Happ нельзя открыть напрямую. Попросите у провайдера обычную HTTPS-ссылку подписки или ссылку на узел."
                        rejectedByEngine > 0 -> "Узлы распознаны, но sing-box не принял их конфигурацию. Проверьте параметры ноды."
                        unsupportedTypes.isNotEmpty() -> "Не поддерживаются типы узлов: ${unsupportedTypes.joinToString()}."
                        kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION -> "В подписке нет поддерживаемых узлов"
                        else -> "Ссылка не содержит поддерживаемый узел"
                    }
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
                notice = buildString {
                    append("Добавлено узлов: $count")
                    if (duplicates > 0) append(" · дубликатов: $duplicates")
                    if (rejectedByEngine > 0) append(" · отклонено проверкой: $rejectedByEngine")
                    if (unsupportedTypes.isNotEmpty()) append(" · не поддерживаются: ${unsupportedTypes.joinToString()}")
                }
            } catch (error: Exception) {
                if (error is CancellationException) throw error
                notice = when {
                    error is SubscriptionBodyTooLarge -> "Подписка превышает допустимый размер 8 МиБ"
                    error is SubscriptionHttpException -> "Сервер подписки вернул HTTP ${error.statusCode}"
                    error is java.net.SocketTimeoutException -> "Сервер подписки не ответил вовремя. Проверьте сеть и попробуйте ещё раз."
                    error is java.net.UnknownHostException -> "Не удалось найти сервер подписки. Проверьте подключение к интернету."
                    error is javax.net.ssl.SSLException -> "Не удалось установить защищённое соединение с сервером подписки."
                    error is IllegalArgumentException && error.message?.contains("размер") == true -> error.message
                    error.message?.contains("cleartext", ignoreCase = true) == true && error.message?.contains("not permitted", ignoreCase = true) == true -> "Незащищённая HTTP-подписка заблокирована Android. Используйте HTTPS."
                    kind == SubscriptionInputKind.REMOTE_SUBSCRIPTION -> "Не удалось загрузить подписку"
                    else -> "Не удалось импортировать ссылку или текст"
                }
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
                QrImportDecision.NoCode -> notice = "На изображении не найден QR-код"
                QrImportDecision.MultipleCodes -> notice = "Найдено несколько QR-кодов. Выберите изображение с одной ссылкой."
                QrImportDecision.Unsupported -> notice = "QR-код не содержит поддерживаемую ссылку или подписку"
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
            }.onSuccess { (nextProfile, message) -> update(nextProfile); notice = message; importRevision++ }
                .onFailure { notice = "Импорт не выполнен: ${it.message}" }
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

    private fun openSubscriptionLink(value: String) {
        val uri = runCatching { Uri.parse(value) }.getOrNull() ?: return
        val allowed = when (uri.scheme?.lowercase()) {
            "https", "http" -> !uri.host.isNullOrBlank()
            "tg" -> uri.host.equals("resolve", true) || uri.host.equals("join", true)
            else -> false
        }
        if (!allowed) { notice = "Ссылка подписки имеет неподдерживаемый формат"; return }
        runCatching { startActivity(Intent(Intent.ACTION_VIEW, uri)) }
            .onFailure { notice = "Не удалось открыть ссылку. Установите Telegram или браузер." }
    }
    private fun hasNetwork(): Boolean {
        val connectivity = getSystemService(ConnectivityManager::class.java)
        val capabilities = connectivity.activeNetwork?.let(connectivity::getNetworkCapabilities) ?: return false
        return capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    }

    private fun connect() {
        val current = profile
        if (importing || current.importedConfig != null || !SubscriptionRefreshPolicy.isStale(current.subscriptionUrl, current.subscriptionFetchedAt, System.currentTimeMillis(), current.subscriptionRefreshHours)) {
            startVpnService()
            return
        }
        notice = "Обновляем подписку…"
        lifecycleScope.launch {
            refreshSubscriptionNodes(current)
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
    private suspend fun refreshSubscriptionNodes(current: ClientProfile) {
        if (current.subscriptionUrls.isEmpty()) return
        val fetched = withContext(Dispatchers.IO) { SubscriptionAutoRefresh.fetchAll(applicationContext, current.subscriptionUrls) }
        if (fetched.isEmpty()) return
        update(SubscriptionRefreshPolicy.applyFetchedSources(profile, fetched, System.currentTimeMillis()))
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

private fun blockedTitle(notice: String?): String = when {
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

@Composable private fun CehoTheme(preference: ThemePreference, content: @Composable () -> Unit) {
    val systemDark = androidx.compose.foundation.isSystemInDarkTheme()
    val useDark = when (preference) {
        ThemePreference.SYSTEM -> systemDark
        ThemePreference.LIGHT -> false
        ThemePreference.DARK -> true
    }
    val scheme = if (useDark) darkColorScheme(
        primary = Color(0xFF6FD39B), onPrimary = Color(0xFF101A16), primaryContainer = Color(0xFF22342B), onPrimaryContainer = Color(0xFFEAF4EE),
        secondary = Color(0xFF6FD39B), onSecondary = Color(0xFF101A16), secondaryContainer = Color(0xFF22342B), onSecondaryContainer = Color(0xFFEAF4EE),
        tertiary = Color(0xFFE8B657), onTertiary = Color(0xFF2A2110),
        background = Color(0xFF101A16), onBackground = Color(0xFFEAF4EE), surface = Color(0xFF18251F), onSurface = Color(0xFFEAF4EE), onSurfaceVariant = Color(0xFF8AA397),
        surfaceContainerLowest = Color(0xFF101A16), surfaceContainerLow = Color(0xFF18251F), surfaceContainer = Color(0xFF18251F),
        surfaceContainerHigh = Color(0xFF22342B), surfaceContainerHighest = Color(0xFF2C4238),
        outline = Color(0xFF5E786B), outlineVariant = Color(0xFF22342B), surfaceVariant = Color(0xFF2C4238), error = Color(0xFFF2857A), onError = Color(0xFF690005), errorContainer = Color(0xFF93000A), onErrorContainer = Color(0xFFFFDAD6),
    ) else lightColorScheme(
        primary = Color(0xFF275B42), onPrimary = Color(0xFFFFFFFF), primaryContainer = Color(0xFFD7E7DA), onPrimaryContainer = Color(0xFF173B29),
        secondary = Color(0xFF456B53), onSecondary = Color(0xFFFFFFFF), secondaryContainer = Color(0xFFE0EADF), onSecondaryContainer = Color(0xFF223B2B),
        tertiary = Color(0xFF79602C), onTertiary = Color(0xFFFFFFFF), tertiaryContainer = Color(0xFFF2E3B9), onTertiaryContainer = Color(0xFF302308),
        background = Color(0xFFF5F2E9), onBackground = Color(0xFF1F2A22), surface = Color(0xFFFFFEFA), onSurface = Color(0xFF1F2A22), onSurfaceVariant = Color(0xFF56645A),
        surfaceContainerLowest = Color(0xFFFFFEFA), surfaceContainerLow = Color(0xFFF9F6EE), surfaceContainer = Color(0xFFF1EEE4),
        surfaceContainerHigh = Color(0xFFECE9DF), surfaceContainerHighest = Color(0xFFE5E2D8),
        outline = Color(0xFF69776C), outlineVariant = Color(0xFFCBD2C8), error = Color(0xFFA3362D), onError = Color(0xFFFFFFFF), errorContainer = Color(0xFFF8DED8), onErrorContainer = Color(0xFF641B16),
    )
    val view = androidx.compose.ui.platform.LocalView.current
    androidx.compose.runtime.SideEffect {
        val activityWindow = (view.context as? Activity)?.window ?: return@SideEffect
        activityWindow.statusBarColor = scheme.background.toArgb()
        activityWindow.navigationBarColor = scheme.background.toArgb()
        WindowCompat.getInsetsController(activityWindow, view).apply {
            isAppearanceLightStatusBars = !useDark
            isAppearanceLightNavigationBars = !useDark
        }
    }
    MaterialTheme(colorScheme = scheme, content = content)
}

internal data class AppChoice(val packageName: String, val label: String, val icon: Drawable)

private fun nodeAllowed(profile: ClientProfile, node: ProxyNode): Boolean =
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
@Composable private fun ClientScreen(profile: ClientProfile, connected: Boolean, connectedSince: Long, serviceRunning: Boolean, blocked: Boolean, measurements: VpnMeasurements, notificationsBlocked: Boolean, onOpenNotificationSettings: () -> Unit, onCheckNode: (String) -> Unit, onCheckNodes: (List<String>) -> Unit, onCancelNodeCheck: () -> Unit, notice: String?, importing: Boolean, importRevision: Int, themePreference: ThemePreference, onThemePreference: (ThemePreference) -> Unit, onboardingCompleted: Boolean, onCompleteOnboarding: () -> Unit, onRestartOnboarding: () -> Unit, appChoices: List<AppChoice>, onNotice: (String) -> Unit, onImportFile: () -> Unit, onImportClipboard: () -> Unit, onScanQr: () -> Unit, onImportQrImage: () -> Unit, onExport: (ProfileExportScope) -> Unit, onTextImport: (String) -> Unit, onConfigImport: (String) -> Unit, onSelectNode: (Int) -> Unit, onApps: (String) -> Unit, onExcludeSelectedApps: (Boolean) -> Unit, onSites: (String) -> Unit, onOnlySites: (Boolean) -> Unit, onDirectCountry: (String) -> Unit, onSaveRouteSettings: (ClientProfile) -> Unit, onConnect: () -> Unit, onStop: () -> Unit, onAlwaysOn: () -> Unit, onHelp: () -> Unit, onExpiryReminders: (Boolean) -> Unit, onSubscriptionRefreshHours: (Int) -> Unit, onOpenSubscriptionLink: (String) -> Unit, onRefreshSubscription: (String) -> Unit, onRemoveSubscription: (String) -> Unit, onRefreshAllSubscriptions: () -> Unit, refreshingSubscriptions: Set<String>) {
    val clipboardContext = LocalContext.current
    ObserveAutomaticUpdates()
    var pendingNodeCheck by remember { mutableStateOf<String?>(null) }
    pendingNodeCheck?.let { id ->
        AlertDialog(onDismissRequest = { pendingNodeCheck = null }, title = { Text("Проверить узел?") }, text = { Text("Измерим задержку и скорость через этот узел. Тест загружает и отправляет данные до 20 секунд. Расход зависит от скорости; лучше запускать по Wi-Fi. Маршруты приложений останутся прежними.") }, confirmButton = { TextButton(onClick = { pendingNodeCheck = null; onCheckNode(id) }, modifier = Modifier.testTag("node-check-confirm")) { Text("Начать проверку") } }, dismissButton = { TextButton(onClick = { pendingNodeCheck = null }) { Text("Отмена") } })
    }
    var subscriptionPromptDismissed by remember { mutableStateOf(false) }
    val needsSubscription = profile.nodes.isEmpty() && profile.importedConfig == null
    if (onboardingCompleted && needsSubscription && !subscriptionPromptDismissed) AlertDialog(
        onDismissRequest = { subscriptionPromptDismissed = true },
        title = { Text("Добавьте подписку") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Без подписки VPN не подключится. Ссылку даёт ваш провайдер.")
                OutlinedButton(onClick = onScanQr, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).testTag("subscription-prompt-qr")) { Text("Сканировать QR-код") }
                OutlinedButton(onClick = onImportFile, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).testTag("subscription-prompt-file")) { Text("Открыть файл") }
            }
        },
        confirmButton = { Button(onClick = onImportClipboard, modifier = Modifier.heightIn(min = 48.dp).testTag("subscription-prompt-paste")) { Text("Вставить из буфера") } },
        dismissButton = { TextButton(onClick = { subscriptionPromptDismissed = true }, modifier = Modifier.testTag("subscription-prompt-later")) { Text("Позже") } },
        modifier = Modifier.testTag("subscription-prompt"),
    )
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
    var showQuickImport by remember { mutableStateOf(false) }
    var nodeSearch by remember { mutableStateOf("") }
    LaunchedEffect(importRevision) {
        if (importRevision > 0) {
            showQuickImport = false
            importValue = ""
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
    val pageTitle = if (page == "Серверы") "Проверка задержки" else page
    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Column(Modifier.fillMaxSize().safeDrawingPadding()) {
            Row(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.background).padding(horizontal = 24.dp, vertical = 16.dp), verticalAlignment = Alignment.CenterVertically) {
                if (page == "Главная") {
                    androidx.compose.foundation.Image(
                        painter = painterResource(R.drawable.cehoproxy_mark_dark),
                        contentDescription = "CehoProxy",
                        modifier = Modifier.size(36.dp).clip(RoundedCornerShape(11.dp)).background(BrandMarkBackground()).padding(5.dp),
                    )
                    Spacer(Modifier.width(10.dp))
                    Text("CehoProxy", fontSize = 22.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
                    Spacer(Modifier.weight(1f))
                    IconButton(onClick = { showAddSheet = true }, modifier = Modifier.testTag("header-add-subscription").semantics { contentDescription = "Добавить подписку" }) { PlusGlyph(MaterialTheme.colorScheme.primary) }
                    IconButton(onClick = { openPage("Настройки") }, modifier = Modifier.testTag("header-settings").semantics { contentDescription = "Настройки" }) { GearGlyph(MaterialTheme.colorScheme.onSurfaceVariant) }
                } else {
                    IconButton(onClick = { backPage() }, modifier = Modifier.size(42.dp).testTag("page-back").semantics { contentDescription = "Назад" }) { BackGlyph(MaterialTheme.colorScheme.onSurface) }
                    Spacer(Modifier.width(8.dp))
                    Text(pageTitle, fontSize = 22.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
                }
            }
            if (!onboardingCompleted) {
                FirstRunOnboarding(
                    profile = profile,
                    onPickCountry = { code -> onSaveRouteSettings(countryOnboardingChoice(profile, code)) },
                    onYoutubeAds = { onSaveRouteSettings(profile.copy(blockYoutubeAds = it)) },
                    onImportFile = onImportFile,
                    onImportClipboard = onImportClipboard,
                    onScanQr = onScanQr,
                    onHelp = onHelp,
                    onFinish = onCompleteOnboarding,
                    notice = notice,
                    modifier = Modifier.weight(1f).fillMaxWidth(),
                )
            } else {
                Column(Modifier.weight(1f).verticalScroll(contentScroll).padding(horizontal = 22.dp).widthIn(max = 820.dp).align(Alignment.CenterHorizontally)) {
                    when (page) {
                        "Главная" -> {
                            val tunnelPackages = TunnelAppsPolicy.packages(profile, appChoices.map { it.packageName }).toSet()
                            val tunnelApps = appChoices.filter { it.packageName in tunnelPackages }
                            val starting = connected || notice == "VPN запускается"
                            val directOnly = profile.nodes.isEmpty() && profile.importedConfig == null && LocalDpiPolicy.activeApps(profile).isNotEmpty()
                            TunnelVortex(
                                apps = tunnelApps,
                                active = starting,
                                connected = connected,
                                running = serviceRunning,
                                blocked = blocked,
                                fullTunnel = profile.excludeSelectedApps,
                                directOnly = directOnly,
                                onClick = { if (serviceRunning) onStop() else onConnect() },
                                modifier = Modifier.fillMaxWidth(),
                            )
                            Column(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                                Text(
                                    when { blocked -> blockedTitle(notice); connected && directOnly -> "Обход DPI подключён"; connected -> "Защищено"; starting -> "Подключаем…"; else -> "VPN выключен" },
                                    color = if (connected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface,
                                    style = MaterialTheme.typography.titleLarge,
                                )
                                Text(
                                    when { blocked -> if (profile.autoRotationEnabled) "Проверка повторится автоматически" else "Выберите другой сервер в списке «Серверы»"; connected -> profile.selectedNode?.let { "Через: ${NodePresentation.routeName(it)}" } ?: "Нажмите на водоворот, чтобы выключить"; starting -> "Подождите несколько секунд"; else -> "Нажмите на водоворот, чтобы включить" },
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    style = MaterialTheme.typography.bodyMedium,
                                    textAlign = TextAlign.Center,
                                )
                                if (connected) VpnUptime(connectedSince)
                            }
                            if (blocked) OutlinedButton(onClick = onConnect, modifier = Modifier.fillMaxWidth().padding(top = 16.dp).heightIn(min = 52.dp).testTag("home-retry")) { Text("Проверить серверы снова") }
                            if (notificationsBlocked) Surface(color = MaterialTheme.colorScheme.errorContainer, shape = RoundedCornerShape(20.dp), modifier = Modifier.fillMaxWidth().padding(top = 16.dp).testTag("home-notifications-off")) {
                                Column(Modifier.padding(16.dp)) {
                                    Text("Уведомления выключены", color = MaterialTheme.colorScheme.onErrorContainer, style = MaterialTheme.typography.titleSmall)
                                    Text("Вы не увидите, что VPN перестал работать. Включите уведомления для CehoProxy.", color = MaterialTheme.colorScheme.onErrorContainer, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(top = 4.dp))
                                    OutlinedButton(onClick = onOpenNotificationSettings, modifier = Modifier.padding(top = 8.dp).heightIn(min = 44.dp)) { Text("Включить") }
                                }
                            }
                            val fastestIndex = profile.nodes.withIndex().filter { nodeAllowed(profile, it.value) && measurements.nodes[it.value.routeId]?.latencyMs != null && measurements.nodes[it.value.routeId]?.failed != true }
                                .minByOrNull { measurements.nodes[it.value.routeId]?.latencyMs ?: Int.MAX_VALUE }?.index
                            if (blocked && !profile.autoRotationEnabled && fastestIndex != null) OutlinedButton(onClick = { onSelectNode(fastestIndex); onConnect() }, modifier = Modifier.fillMaxWidth().padding(top = 8.dp).heightIn(min = 52.dp).testTag("home-switch-fastest")) { Text("Подключиться к самому быстрому") }
                            (profile.selectedNode ?: profile.nodes.firstOrNull())?.let { node ->
                                val expires = node.subscriptionExpiresAt ?: node.subscriptionMetadata.expiresAtEpochSeconds
                                val url = SubscriptionRemovalPolicy.urlFor(profile, node.subscriptionMetadata)
                                if (url != null || node.subscriptionMetadata.usedBytes != null || node.subscriptionMetadata.totalBytes != null || expires != null) {
                                    Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(22.dp), modifier = Modifier.fillMaxWidth().padding(top = 20.dp).testTag("home-subscription")) {
                                        SubscriptionCard(
                                            metadata = node.subscriptionMetadata,
                                            expiresAt = expires,
                                            fetchedAt = url?.let { profile.subscriptionFetchedAtByUrl[it] },
                                            serverCount = url?.let { u -> profile.nodes.count { SubscriptionMembership.belongs(it, u, profile.subscriptionUrls) } } ?: profile.nodes.size,
                                            onOpenLink = onOpenSubscriptionLink,
                                            onShare = url?.let { u -> { shareCandidate = u } },
                                            onSettings = url?.let { u -> { subscriptionPageUrl = u; openPage("Подписка") } },
                                            modifier = Modifier.padding(18.dp),
                                        )
                                    }
                                }
                            }
                            Row(Modifier.fillMaxWidth().padding(top = 24.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                                Text("СЕРВЕРЫ", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
                                if (profile.nodes.size > 3) TextButton(onClick = { openPage("Серверы") }, modifier = Modifier.testTag("home-all-nodes")) { Text("Все серверы ›") }
                            }
                            if (profile.nodes.isEmpty()) OutlinedButton(onClick = { showAddSheet = true }, modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp).testTag("home-add-node")) { Text("Добавить ссылку от провайдера") }
                            else profile.nodes.withIndex().take(3).forEach { (index, node) ->
                                HomeNodeRow(
                                    node = node,
                                    selected = index == profile.selected,
                                    measurement = measurements.nodes[node.routeId],
                                    enabled = nodeAllowed(profile, node),
                                    onClick = { if (nodeAllowed(profile, node)) onSelectNode(index) },
                                    modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp).testTag("home-node-option:$index"),
                                )
                            }
                            if (notice != null && !showQuickImport && !showAddSheet) Text(notice, color = if (notice.contains("ошиб", true) || notice.contains("не выдано", true)) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary, modifier = Modifier.padding(top = 12.dp))
                        }
                        "Подписки" -> {
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
                            Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(22.dp), modifier = Modifier.fillMaxWidth().padding(top = 14.dp)) {
                                Column(Modifier.padding(18.dp)) {
                                    Text("Добавить подписку", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface)
                                    Text("Ссылка, QR-код или файл конфигурации", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 2.dp))
                                    Row(Modifier.fillMaxWidth().padding(top = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                        PillButton("Вставить ссылку", { showQuickImport = true }, Modifier.weight(1f).testTag("subscriptions-add"))
                                        PillButton("Сканировать QR", onScanQr, Modifier.weight(1f))
                                    }
                                }
                            }
                            if (profile.subscriptionUrls.isNotEmpty()) {
                                SectionLabel("АВТООБНОВЛЕНИЕ")
                                SettingsGroup {
                                    Column(Modifier.padding(16.dp)) {
                                        Text("Обновлять каждые", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurface)
                                        Text("Новые узлы подтягиваются сами.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 2.dp))
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
                            SettingsGroup {
                                SettingsRow(
                                    title = "Весь трафик через VPN",
                                    value = null,
                                    switchChecked = profile.excludeSelectedApps,
                                    onSwitch = onExcludeSelectedApps,
                                    modifier = Modifier.testTag("route-all-traffic"),
                                )
                            }
                            Text(if (profile.excludeSelectedApps) "Кроме отмеченных ниже" else "Только отмеченные ниже", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(start = 6.dp, top = 6.dp))
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
                                    Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(22.dp), modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
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
                                    Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(22.dp), modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
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
                            SectionLabel("РЕКЛАМА")
                            SettingsGroup {
                                SettingsRow(
                                    title = "Реклама YouTube",
                                    value = null,
                                    switchChecked = profile.blockYoutubeAds,
                                    onSwitch = { onSaveRouteSettings(profile.copy(blockYoutubeAds = it)) },
                                    modifier = Modifier.testTag("home-block-youtube-ads"),
                                )
                            }
                            Text("Блокирует рекламные сайты. Реклама внутри видео может остаться.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(start = 6.dp, top = 6.dp))
                            Button(onClick = {
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
                            }, modifier = Modifier.padding(top = 16.dp).fillMaxWidth().heightIn(min = 48.dp).testTag("routing-save")) { Text("Сохранить и применить") }
                            if (notice != null && !showQuickImport) Text(notice, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(top = 10.dp))
                        }
                        "Подписка" -> {
                            val url = subscriptionPageUrl?.takeIf { it in profile.subscriptionUrls }
                            if (url == null) EmptyInstruction("Подписка не найдена", "Откройте список подписок и выберите её снова.")
                            else {
                                val node = profile.nodes.firstOrNull { SubscriptionRemovalPolicy.urlFor(profile, it.subscriptionMetadata) == url }
                                val refreshing = url in refreshingSubscriptions
                                Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(22.dp), modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
                                    SubscriptionCard(
                                        metadata = node?.subscriptionMetadata ?: SubscriptionMetadata(sourceHost = SubscriptionRemovalPolicy.hostOf(url)),
                                        expiresAt = node?.let { it.subscriptionExpiresAt ?: it.subscriptionMetadata.expiresAtEpochSeconds },
                                        fetchedAt = profile.subscriptionFetchedAtByUrl[url],
                                        serverCount = profile.nodes.count { SubscriptionMembership.belongs(it, url, profile.subscriptionUrls) },
                                        onOpenLink = onOpenSubscriptionLink,
                                        onShare = null,
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
                                        value = if (profile.subscriptionRefreshHours == 0) "Выкл" else "каждые ${profile.subscriptionRefreshHours} ч",
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
                                Surface(onClick = { removeCandidate = url }, color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(22.dp), modifier = Modifier.fillMaxWidth().padding(top = 18.dp).testTag("subscription-remove")) {
                                    Box(Modifier.fillMaxWidth().padding(vertical = 14.dp), contentAlignment = Alignment.Center) {
                                        Text("Удалить подписку", color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyLarge)
                                    }
                                }
                            }
                        }
                        "Серверы" -> {
                            Text("${profile.nodes.size} серверов", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 8.dp, bottom = 12.dp))
                            if (profile.nodes.size > 1) Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(20.dp), modifier = Modifier.fillMaxWidth().padding(bottom = 12.dp)) {
                                Row(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                                    Column(Modifier.weight(1f).padding(end = 8.dp)) {
                                        Text("Автоматическое переключение", color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.titleSmall)
                                        Text(if (profile.autoRotationEnabled) "Если узел перестаёт отвечать, CehoProxy сам подключает следующий рабочий — в том числе из других подписок." else "Выключено: используется только выбранный вами узел.", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
                                    }
                                    Switch(checked = profile.autoRotationEnabled, onCheckedChange = { on -> onSaveRouteSettings(profile.copy(autoRotationEnabled = on)) }, modifier = Modifier.testTag("servers-auto-switch").semantics { contentDescription = "Автоматическое переключение" })
                                }
                            }
                            profile.nodes.withIndex().filter { nodeAllowed(profile, it.value) && measurements.nodes[it.value.routeId]?.latencyMs != null && measurements.nodes[it.value.routeId]?.failed != true }
                                .minByOrNull { measurements.nodes[it.value.routeId]?.latencyMs ?: Int.MAX_VALUE }?.let { fastest ->
                                OutlinedButton(onClick = { onSelectNode(fastest.index) }, modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp).heightIn(min = 48.dp).testTag("subscriptions-fastest")) {
                                    Text("Самый быстрый · ${measurements.nodes[fastest.value.routeId]?.latencyMs} мс")
                                }
                            }
                            val allRouteIds = profile.nodes.map { it.routeId }
            Row(Modifier.fillMaxWidth().padding(bottom = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { onCheckNodes(allRouteIds) }, enabled = allRouteIds.isNotEmpty(), modifier = Modifier.weight(1f).heightIn(min = 48.dp).testTag("servers-check-all")) { Text("Проверить все") }
                OutlinedButton(onClick = onRefreshAllSubscriptions, enabled = refreshingSubscriptions.isEmpty() && profile.subscriptionUrls.isNotEmpty(), modifier = Modifier.weight(1f).heightIn(min = 48.dp).testTag("servers-refresh-all")) { Text(if (refreshingSubscriptions.isEmpty()) "Обновить подписки" else "Обновляем…") }
            }
            if (!measurements.connected) Text("Без VPN: TCP-задержка до адреса сервера. UDP-узлы (Hysteria2) проверяются только при включённом VPN", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(bottom = 8.dp))
            if (notice != null && !showQuickImport) Text(notice, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(bottom = 12.dp))
                            if (profile.nodes.isEmpty()) EmptyInstruction("Серверов пока нет", "Добавьте подписку или ссылку от провайдера.")
                            else {
                                OutlinedTextField(value = nodeSearch, onValueChange = { nodeSearch = it }, label = { Text("Найти узел или подписку") }, singleLine = true, modifier = Modifier.fillMaxWidth().testTag("subscriptions-search"))
                                val visibleNodes = profile.nodes.withIndex().filter { it.value.name.contains(nodeSearch, true) || NodePresentation.subscriptionSource(it.value).contains(nodeSearch, true) }
                                visibleNodes.forEachIndexed { position, indexed ->
                                    val (index, node) = indexed
                                    val source = NodePresentation.subscriptionSource(node)
                                    val groupKey = Triple(source, node.subscriptionMetadata, node.subscriptionExpiresAt)
                                    val subscriptionKey = NodePresentation.subscriptionKey(node)
                                    val previousNode = visibleNodes.getOrNull(position - 1)?.value
                                    val previousGroupKey = previousNode?.let { Triple(NodePresentation.subscriptionSource(it), it.subscriptionMetadata, it.subscriptionExpiresAt) }
                                    if (groupKey != previousGroupKey) {
                                        Column(Modifier.fillMaxWidth().padding(top = 22.dp, bottom = 8.dp)) {
                                            Text("ПОДПИСКА", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.primary)
                                            Row(verticalAlignment = Alignment.CenterVertically) {
                                                Column(Modifier.weight(1f)) {
                                                    Text(source.removePrefix("Подписка: "), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                                    SubscriptionRemovalPolicy.urlFor(profile, node.subscriptionMetadata)?.let { url -> profile.subscriptionFetchedAtByUrl[url]?.let { at -> Text(SubscriptionRefreshPolicy.fetchedLabel(at, System.currentTimeMillis()), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.testTag("subscription-fetched:$index")) } }
                                                }
                                                OutlinedButton(onClick = { expandedSubscriptionSource = if (expandedSubscriptionSource == groupKey) null else groupKey }, contentPadding = PaddingValues(horizontal = 12.dp), modifier = Modifier.heightIn(min = 36.dp).testTag("subscription-details-toggle:$index")) { Text(if (expandedSubscriptionSource == groupKey) "Скрыть" else "Сведения") }
                                                OutlinedButton(onClick = { collapsedSubscriptions = if (subscriptionKey in collapsedSubscriptions) collapsedSubscriptions - subscriptionKey else collapsedSubscriptions + subscriptionKey }, contentPadding = PaddingValues(horizontal = 12.dp), modifier = Modifier.heightIn(min = 36.dp).testTag("subscription-collapse:$index")) { Text(if (subscriptionKey in collapsedSubscriptions) "Развернуть" else "Свернуть") }
                                            }
                                            val groupRouteIds = profile.nodes.filter { NodePresentation.subscriptionKey(it) == subscriptionKey }.map { it.routeId }
                                            val groupUrl = SubscriptionRemovalPolicy.urlFor(profile, node.subscriptionMetadata)
                                            val groupRefreshing = groupUrl != null && groupUrl in refreshingSubscriptions
                                            Row(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                                OutlinedButton(onClick = { onCheckNodes(groupRouteIds) }, enabled = groupRouteIds.isNotEmpty(), modifier = Modifier.weight(1f).heightIn(min = 40.dp).testTag("subscription-check-group:$subscriptionKey")) { Text("Проверить") }
                                                OutlinedButton(onClick = { groupUrl?.let(onRefreshSubscription) }, enabled = groupUrl != null && !groupRefreshing, modifier = Modifier.weight(1f).heightIn(min = 40.dp).testTag("subscription-refresh-group:$subscriptionKey")) { Text(if (groupRefreshing) "Обновляем…" else "Обновить") }
                                            }
                                            Row(Modifier.fillMaxWidth().padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                                                Text("Использовать подписку", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
                                                Switch(checked = subscriptionKey !in profile.disabledSubscriptions, onCheckedChange = { enabled -> onSaveRouteSettings(subscriptionToggled(profile, subscriptionKey, enabled)) }, modifier = Modifier.testTag("subscription-enabled:$index").semantics { contentDescription = "Использовать подписку" })
                                            }
                                            if (profile.autoRotationEnabled && profile.nodes.size > 1) Row(Modifier.fillMaxWidth().padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                                                Text("Автопереключение в этой подписке", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(1f))
                                                Switch(checked = subscriptionKey !in profile.autoDisabledSubscriptions, onCheckedChange = { on -> onSaveRouteSettings(profile.copy(autoDisabledSubscriptions = if (on) profile.autoDisabledSubscriptions - subscriptionKey else profile.autoDisabledSubscriptions + subscriptionKey)) }, enabled = subscriptionKey !in profile.disabledSubscriptions, modifier = Modifier.testTag("subscription-auto:$index").semantics { contentDescription = "Автопереключение в этой подписке" })
                                            }
                                        }
                                        if (expandedSubscriptionSource == groupKey) SubscriptionDetails(node.subscriptionMetadata, onOpenSubscriptionLink)
                                    }
                                    if (subscriptionKey !in collapsedSubscriptions) DraggableNodeRow(node.raw, onDragEnd = { steps -> onSaveRouteSettings(moveNodeInList(profile, node.raw, steps)) }) { rowModifier ->
                                        NodeRow(node, index == profile.selected, { if (nodeAllowed(profile, node)) onSelectNode(index) }, measurements.nodes[node.routeId], if (measurements.connected && profile.importedConfig == null) ({ if (measurements.nodes[node.routeId]?.testing == true) onCancelNodeCheck() else pendingNodeCheck = node.routeId }) else null, rowModifier.testTag("home-node-option:$index"),
                                            autoEnabled = node.routeId !in profile.autoExcludedRouteIds,
                                            onAutoToggle = { on -> onSaveRouteSettings(profile.copy(autoExcludedRouteIds = if (on) profile.autoExcludedRouteIds - node.routeId else profile.autoExcludedRouteIds + node.routeId)) },
                                            autoSwitchTag = "node-auto-switch:$index",
                                            excludedLabel = when {
                                                NodePresentation.subscriptionKey(node) in profile.disabledSubscriptions -> "Подписка отключена"
                                                RoutingConfig.countryExcluded(node, profile.excludedCountrySets) -> "Страна исключена"
                                                else -> null
                                            })
                                    }
                                }
                                if (visibleNodes.isEmpty()) Text("Ничего не найдено. Измените запрос.", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(vertical = 20.dp))
                            }
                        }
                        "Настройки" -> {
                            SectionLabel("ИНТЕРФЕЙС")
                            SettingsGroup {
                                Column(Modifier.padding(horizontal = 16.dp, vertical = 14.dp)) {
                                    Text("Тема", style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurface)
                                    Row(Modifier.fillMaxWidth().padding(top = 10.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                        ThemePreference.entries.forEach { option ->
                                            FilterChip(selected = themePreference == option, onClick = { onThemePreference(option) }, label = { Text(option.label) }, modifier = Modifier.testTag("theme-${option.storageValue}").focusable())
                                        }
                                    }
                                }
                            }
                            SectionLabel("ТУННЕЛЬ")
                            SettingsGroup {
                                SettingsRow(title = "Маршрутизация", onClick = { openPage("Маршрутизация") }, modifier = Modifier.testTag("settings-routing"))
                                SettingsRow(title = "Постоянный VPN", value = "Автозапуск", onClick = { showAlwaysOnHelp = !showAlwaysOnHelp })
                            }
                            if (showAlwaysOnHelp) {
                                Text("Android включит VPN сам после перезагрузки телефона.", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 10.dp))
                                OutlinedButton(onClick = onAlwaysOn, modifier = Modifier.fillMaxWidth().padding(top = 8.dp).heightIn(min = 48.dp)) { Text("Открыть настройки Android") }
                            }
                            SectionLabel("СЕРВЕРЫ")
                            SettingsGroup {
                                SettingsRow(title = "Подписки", onClick = { openPage("Подписки") }, modifier = Modifier.testTag("settings-subscriptions"))
                                SettingsRow(title = "Проверка задержки", onClick = { openPage("Серверы") }, modifier = Modifier.testTag("settings-latency"))
                                SettingsRow(title = "Напоминать о сроке", switchChecked = profile.expiryRemindersEnabled, onSwitch = onExpiryReminders)
                            }
                            SectionLabel("ДАННЫЕ")
                            SettingsGroup {
                                Row(Modifier.fillMaxWidth().padding(16.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                                    OutlinedButton(onClick = onImportFile, modifier = Modifier.weight(1f).heightIn(min = 48.dp)) { Text("Импорт") }
                                    Button(onClick = { openPage("Экспорт") }, modifier = Modifier.weight(1f).heightIn(min = 48.dp)) { Text("Экспорт") }
                                }
                                SettingsRow(title = "Проверить соединение", onClick = { scope.launch { diagnostics = withContext(Dispatchers.IO) { ConnectionDiagnostics.run(context) } } }, modifier = Modifier.testTag("settings-diagnostics"))
                                SettingsRow(title = "Поделиться журналом", onClick = { shareEventLog(context) }, modifier = Modifier.testTag("settings-share-log"))
                                SettingsRow(title = "Помощь и поддержка", onClick = { openPage("Помощь") })
                                if (android.os.Build.MANUFACTURER.equals("samsung", true)) {
                                    SettingsRow(
                                        title = "Скрыть значок VPN на Samsung",
                                        onClick = {
                                            runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, android.net.Uri.parse("samsungapps://ProductDetail/com.samsung.android.qstuner"))) }
                                                .recoverCatching { context.startActivity(Intent(Intent.ACTION_VIEW, android.net.Uri.parse("https://apps.samsung.com/appquery/appDetail.as?appId=com.samsung.android.qstuner"))) }
                                                .onFailure { onNotice("Не удалось открыть Galaxy Store. Найдите QuickStar в приложении Good Lock.") }
                                        },
                                    )
                                }
                                SettingsRow(title = "Пройти знакомство заново", color = MaterialTheme.colorScheme.error, onClick = { pageHistory.clear(); page = "Главная"; onRestartOnboarding() }, modifier = Modifier.testTag("restart-onboarding"))
                            }
                            SectionLabel("О ПРИЛОЖЕНИИ")
                            SettingsGroup {
                                Text("Версия ${BuildConfig.VERSION_NAME}", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(start = 16.dp, top = 14.dp))
                                UpdateSettingsSection()
                            }
                        }
                        "Помощь" -> {
                            Text("Подскажем, как получить ссылку и подключиться", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp, bottom = 20.dp))
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
                            Text("Выберите, что перенести. Файлы могут содержать ссылки подписок или собственную конфигурацию с данными доступа.", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp, bottom = 18.dp))
                            ProfileExportScope.entries.forEach { scope ->
                                OutlinedButton(
                                    onClick = { onExport(scope) },
                                    modifier = Modifier.fillMaxWidth().padding(bottom = 10.dp).heightIn(min = 64.dp),
                                ) {
                                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                        Text(scope.title)
                                        Text(
                                            when (scope) {
                                                ProfileExportScope.SETTINGS -> "Правила, приложения и конфигурация"
                                                ProfileExportScope.SUBSCRIPTIONS -> "Узлы и данные подписок"
                                                ProfileExportScope.ALL -> "Настройки и подписки вместе"
                                            },
                                            style = MaterialTheme.typography.bodySmall,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        )
                                    }
                                }
                            }
                        }
                    }
                    Spacer(Modifier.height(24.dp))
                }
            }
        }
    }
    if (showAddSheet) ModalBottomSheet(onDismissRequest = { showAddSheet = false }, containerColor = MaterialTheme.colorScheme.surfaceContainer) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp).padding(bottom = 28.dp)) {
            Text("Добавить подписку", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
            Text("Серверы загрузятся сами, подписка появится в списке", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 4.dp))
            SheetOption(title = "Вставить ссылку", subtitle = "Ссылка или текст конфигурации", glyph = { LinkGlyph(MaterialTheme.colorScheme.primary) }, modifier = Modifier.testTag("add-sheet-paste"), onClick = { showAddSheet = false; showQuickImport = true })
            SheetOption(title = "Сканировать QR-код", subtitle = "Камера телефона", glyph = { ScanGlyph(MaterialTheme.colorScheme.primary) }, modifier = Modifier.testTag("add-sheet-qr"), onClick = { showAddSheet = false; onScanQr() })
            SheetOption(title = "Открыть файл", subtitle = "Конфигурация .json или .txt", glyph = { FileGlyph(MaterialTheme.colorScheme.primary) }, modifier = Modifier.testTag("add-sheet-file"), onClick = { showAddSheet = false; onImportFile() })
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
    if (showQuickImport) {
        AlertDialog(
            onDismissRequest = { if (!importing) showQuickImport = false },
            title = { Text("Добавить подписку") },
            text = {
                Column(Modifier.verticalScroll(rememberScrollState())) {
                    Text("Вставьте ссылку или выберите способ добавления", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodyMedium)
                    OutlinedTextField(value = importValue, onValueChange = { importValue = it }, label = { Text("Ссылка или конфигурация") }, enabled = !importing, modifier = Modifier.fillMaxWidth().padding(top = 12.dp).testTag("import-input"), minLines = 2, maxLines = 4)
                    if (importing) {
                        LinearProgressIndicator(modifier = Modifier.fillMaxWidth().padding(top = 12.dp).testTag("import-progress"))
                        Text("Загружаем и проверяем узлы…", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp))
                    }
                    Row(Modifier.fillMaxWidth().padding(top = 12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(onClick = onImportClipboard, enabled = !importing, modifier = Modifier.weight(1f)) { Text("Из буфера") }
                        OutlinedButton(onClick = onScanQr, enabled = !importing, modifier = Modifier.weight(1f)) { Text("Камера QR") }
                    }
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        OutlinedButton(onClick = onImportFile, enabled = !importing, modifier = Modifier.weight(1f)) { Text("Файл") }
                        OutlinedButton(onClick = onImportQrImage, enabled = !importing, modifier = Modifier.weight(1f)) { Text("QR-картинка") }
                    }
                    notice?.let { Text(it, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp)) }
                }
            },
            confirmButton = { Button(onClick = { onTextImport(importValue) }, enabled = !importing && importValue.isNotBlank(), modifier = Modifier.testTag("import-submit")) { Text("Добавить") } },
            dismissButton = { TextButton(onClick = { showQuickImport = false }, enabled = !importing) { Text("Отмена") } },
        )
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
    onYoutubeAds: (Boolean) -> Unit,
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
    LaunchedEffect(step, profile.nodes.isNotEmpty()) { if (step == 1 && profile.nodes.isNotEmpty()) onFinish() }
    Column(modifier.verticalScroll(rememberScrollState()).padding(horizontal = 24.dp, vertical = 12.dp).widthIn(max = 760.dp)) {
        if (step == 0) {
            Text("Какая у вас страна?", style = MaterialTheme.typography.headlineMedium, color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.padding(top = 24.dp))
            Text("Сайты этой страны открываются без VPN. Если страны нет в списке, выберите «Другая страна».", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp, bottom = 10.dp))
            CountryRouting.countries.forEach { option ->
                OnboardingCountryRow(
                    title = "${NodePresentation.flag(option.code.uppercase()) ?: ""} ${option.name}",
                    subtitle = null,
                    selected = profile.directCountryCode == option.code,
                    tag = "onboarding-country:${option.code}",
                    onClick = { onPickCountry(option.code) },
                )
            }
            OnboardingCountryRow(title = "Другая страна", subtitle = null, selected = profile.directCountryCode.isEmpty(), tag = "onboarding-country:none", onClick = { onPickCountry("") })
            if (profile.directCountryCode == "ru") {
                Surface(modifier = Modifier.fillMaxWidth().padding(top = 16.dp).testTag("onboarding-youtube"), color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(16.dp)) {
                    Row(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text("Реклама YouTube", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
                            Text("Блокирует рекламные сайты. Реклама внутри видео может остаться.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Switch(checked = profile.blockYoutubeAds, onCheckedChange = onYoutubeAds, modifier = Modifier.testTag("onboarding-youtube-switch"))
                    }
                }
            }
            Button(onClick = { step = 1 }, modifier = Modifier.fillMaxWidth().padding(top = 18.dp).heightIn(min = 52.dp).testTag("onboarding-next")) { Text("Далее") }
        } else {
            Text("Добавьте подписку", style = MaterialTheme.typography.headlineMedium, color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.padding(top = 24.dp))
            Text("Вставьте ссылку от провайдера. Остальное настроится само.", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 8.dp, bottom = 18.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Button(onClick = onImportClipboard, modifier = Modifier.weight(3f).heightIn(min = 56.dp).testTag("onboarding-paste")) { Text("Вставить из буфера") }
                Spacer(Modifier.width(8.dp))
                Box(Modifier.weight(2f)) {
                    OutlinedButton(onClick = { showOther = true }, modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp).testTag("onboarding-other")) { Text("Другое") }
                    DropdownMenu(expanded = showOther, onDismissRequest = { showOther = false }) {
                        DropdownMenuItem(text = { Text("Сканировать QR-код") }, onClick = { showOther = false; onScanQr() })
                        DropdownMenuItem(text = { Text("Открыть файл") }, onClick = { showOther = false; onImportFile() })
                        DropdownMenuItem(text = { Text("Помощь в Telegram") }, onClick = { showOther = false; onHelp() })
                    }
                }
            }
            notice?.let { Text(it, color = if (it.contains("ошиб", true)) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary, modifier = Modifier.padding(top = 12.dp)) }
            TextButton(onClick = onFinish, modifier = Modifier.align(Alignment.CenterHorizontally).padding(top = 12.dp).heightIn(min = 48.dp).testTag("onboarding-later")) { Text("Добавить позже") }
        }
    }
}

@Composable
private fun OnboardingCountryRow(title: String, subtitle: String?, selected: Boolean, tag: String, onClick: () -> Unit) {
    Surface(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth().padding(top = 8.dp).heightIn(min = 64.dp).testTag(tag),
        color = if (selected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceContainer,
        shape = RoundedCornerShape(16.dp),
    ) {
        Row(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(title, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
                subtitle?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
            }
            RadioButton(selected = selected, onClick = null)
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
private fun TunnelVortex(
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
        modifier = modifier.height(204.dp).focusable().testTag("home-vortex").semantics {
            contentDescription = if (running) "Служба VPN запущена. Нажмите, чтобы отключить." else "Воронка CehoProxy. Нажмите, чтобы подключить."
        },
        color = MaterialTheme.colorScheme.background,
        shape = RoundedCornerShape(24.dp),
    ) {
        BoxWithConstraints(Modifier.fillMaxSize()) {
            val density = androidx.compose.ui.platform.LocalDensity.current
            val widthPx = with(density) { maxWidth.toPx() }
            val heightPx = with(density) { maxHeight.toPx() }
            val center = Offset(widthPx / 2f, heightPx * .4f)
            val radius = with(density) { 76.dp.toPx() }.coerceAtMost(widthPx * .34f)
            val moving = active && motionEnabled
            val turn = if (moving) phase * 2f * Math.PI.toFloat() else 0f
            Canvas(Modifier.fillMaxSize()) {
                listOf(.98f, .76f, .55f).forEachIndexed { index, scale ->
                    drawCircle(
                        color = when (index) { 0 -> themeColors.primary; 1 -> themeColors.secondary; else -> themeColors.primary.copy(alpha = .72f) },
                        radius = radius * scale,
                        center = center,
                        style = Stroke(width = if (index == 0) 2.dp.toPx() else 3.dp.toPx()),
                    )
                }
                repeat(VORTEX_ARMS) { arm ->
                    val spiral = Path().apply {
                        val steps = 96
                        for (step in 0..steps) {
                            val point = vortexPoint(center, radius, step / steps.toFloat(), arm, turn)
                            if (step == 0) moveTo(point.x, point.y) else lineTo(point.x, point.y)
                        }
                    }
                    drawPath(spiral, themeColors.secondary, style = Stroke(width = 2.dp.toPx(), cap = StrokeCap.Round))
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
                    color = MaterialTheme.colorScheme.primaryContainer, shape = RoundedCornerShape(13.dp),
                    border = androidx.compose.foundation.BorderStroke(1.dp, MaterialTheme.colorScheme.secondary),
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

@Composable private fun NodeCountryMark(country: VerifiedNodeCountry?, nameCode: String?) {
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

@Composable private fun NodeRow(node: ProxyNode, selected: Boolean, onClick: () -> Unit, measurement: NodeMeasurement? = null, onCheck: (() -> Unit)? = null, modifier: Modifier = Modifier, autoEnabled: Boolean? = null, onAutoToggle: ((Boolean) -> Unit)? = null, autoSwitchTag: String = "", excludedLabel: String? = null) {
    val textColor = if (selected) MaterialTheme.colorScheme.onPrimaryContainer else MaterialTheme.colorScheme.onSurface
    val subColor = if (selected) MaterialTheme.colorScheme.onPrimaryContainer else MaterialTheme.colorScheme.onSurfaceVariant
    Surface(
        onClick = onClick,
        modifier = modifier.fillMaxWidth().padding(vertical = 5.dp).heightIn(min = 68.dp).alpha(if (excludedLabel != null) .45f else 1f),
        shape = RoundedCornerShape(20.dp),
        color = if (selected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceContainer,
    ) {
        Row(Modifier.padding(horizontal = 16.dp, vertical = 14.dp), verticalAlignment = Alignment.CenterVertically) {
            NodeCountryMark(measurement?.verifiedCountry, NodeCountry.resolve(node.name))
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Text(NodePresentation.displayName(node.name), color = textColor, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(node.type.uppercase() + " · " + NodeMetricsPolicy.summary(measurement), color = subColor, style = MaterialTheme.typography.bodySmall)
                excludedLabel?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.labelSmall) }
            }
            if (onAutoToggle != null) Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Text("Авто", style = MaterialTheme.typography.labelSmall, color = subColor)
                Switch(checked = autoEnabled == true, onCheckedChange = onAutoToggle, modifier = if (autoSwitchTag.isNotEmpty()) Modifier.testTag(autoSwitchTag) else Modifier)
            }
            if (onCheck != null) OutlinedButton(onClick = onCheck, contentPadding = PaddingValues(horizontal = 12.dp), modifier = Modifier.heightIn(min = 36.dp)) { Text(if (measurement?.testing == true) "Отмена" else "Проверить") }
            else if (selected) Text("Выбран", color = MaterialTheme.colorScheme.onPrimaryContainer, style = MaterialTheme.typography.labelSmall, modifier = Modifier.padding(start = 8.dp))
        }
    }
}

@Composable private fun SubscriptionCard(metadata: SubscriptionMetadata, expiresAt: Long?, fetchedAt: Long?, serverCount: Int, onOpenLink: (String) -> Unit, onShare: (() -> Unit)?, onSettings: (() -> Unit)?, modifier: Modifier = Modifier) {
    val telegramBlue = Color(0xFF2AABEE)
    Column(modifier.fillMaxWidth()) {
        Text("ПОДПИСКА", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.primary)
        Row(Modifier.fillMaxWidth().padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(metadata.title ?: metadata.sourceHost ?: "Подписка", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
            telegramLink(metadata)?.let { link ->
                IconButton(onClick = { onOpenLink(link) }, modifier = Modifier.size(34.dp).testTag("home-subscription-telegram").semantics { contentDescription = "Telegram подписки" }) { TelegramGlyph(telegramBlue) }
            }
            Spacer(Modifier.weight(1f))
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
    Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(22.dp), modifier = modifier.fillMaxWidth().testTag("subscription-item:$index")) {
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

@Composable private fun HomeNodeRow(node: ProxyNode, selected: Boolean, measurement: NodeMeasurement?, enabled: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val ping = measurement?.latencyMs
    Surface(
        onClick = onClick,
        modifier = modifier.heightIn(min = 64.dp).alpha(if (enabled) 1f else .45f),
        shape = RoundedCornerShape(16.dp),
        color = if (selected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceContainer,
    ) {
        Row(Modifier.padding(horizontal = 14.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            NodeCountryMark(measurement?.verifiedCountry, NodeCountry.resolve(node.name))
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Text(NodePresentation.displayName(node.name), color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.titleSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(node.type.uppercase(), color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
            }
            Text(
                ping?.let { "$it мс" } ?: "—",
                style = MaterialTheme.typography.labelLarge,
                color = if (ping != null && ping > 800) MaterialTheme.colorScheme.tertiary else MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(start = 8.dp),
            )
        }
    }
}

@Composable private fun PillButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, textColor: Color = MaterialTheme.colorScheme.onSurface) {
    Surface(
        onClick = onClick,
        enabled = enabled,
        shape = RoundedCornerShape(14.dp),
        color = MaterialTheme.colorScheme.surfaceContainerHigh,
        modifier = modifier.heightIn(min = 40.dp),
    ) {
        Box(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 10.dp), contentAlignment = Alignment.Center) {
            Text(text, style = MaterialTheme.typography.labelLarge, color = if (enabled) textColor else MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable private fun SectionLabel(text: String) {
    Text(text, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.fillMaxWidth().padding(start = 6.dp, top = 22.dp, bottom = 8.dp))
}

@Composable private fun SettingsGroup(content: @Composable ColumnScope.() -> Unit) {
    Surface(color = MaterialTheme.colorScheme.surfaceContainer, shape = RoundedCornerShape(22.dp), modifier = Modifier.fillMaxWidth()) {
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
        if (onClick != null && switchChecked == null) Text("›", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(start = 8.dp))
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

private fun subscriptionDateLabel(epochSeconds: Long): String =
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

@Composable private fun VpnUptime(sinceMillis: Long) {
    if (sinceMillis <= 0L) return
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(sinceMillis) { while (true) { now = System.currentTimeMillis(); kotlinx.coroutines.delay(1000) } }
    Text("Работает ${UptimeFormat.label(now - sinceMillis)}", style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.secondary, modifier = Modifier.padding(top = 6.dp).testTag("home-uptime"))
}

@Composable private fun RefreshGlyph(color: Color, spinning: Boolean = false) {
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

@Composable private fun PlusGlyph(color: Color) {
    Canvas(Modifier.size(24.dp)) {
        val s = size.minDimension / 24f
        drawLine(color, Offset(12f * s, 5f * s), Offset(12f * s, 19f * s), strokeWidth = 2f * s, cap = StrokeCap.Round)
        drawLine(color, Offset(5f * s, 12f * s), Offset(19f * s, 12f * s), strokeWidth = 2f * s, cap = StrokeCap.Round)
    }
}

@Composable private fun GearGlyph(color: Color) {
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

private fun formatBytes(bytes: Long): String {
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
