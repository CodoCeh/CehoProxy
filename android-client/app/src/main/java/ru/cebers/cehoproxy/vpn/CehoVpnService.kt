package ru.cebers.cehoproxy.vpn

import android.annotation.SuppressLint
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.VpnService
import android.net.IpPrefix
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.LinkProperties
import android.net.NetworkRequest
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.os.ParcelFileDescriptor
import androidx.core.app.NotificationCompat
import io.nekohasekai.libbox.*
import ru.cebers.cehoproxy.core.*
import java.io.File
import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.URL
import java.net.ServerSocket
import java.net.InetAddress
import java.net.NetworkInterface
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.Executors
import java.util.concurrent.RejectedExecutionException
import java.util.concurrent.ConcurrentHashMap

class CehoVpnService : VpnService(), PlatformInterface, CommandServerHandler {
    companion object {
        const val ACTION_START = "ru.cebers.cehoproxy.START"
        const val ACTION_STOP = "ru.cebers.cehoproxy.STOP"
        const val ACTION_STATUS = "ru.cebers.cehoproxy.STATUS"
        private const val CHANNEL = "cehoproxy-vpn"
        private const val NOTIFICATION_ID = 41
        private const val PREFS = "vpn_status"
        private const val SELECT_TIMEOUT_MILLIS = 8_000L
        private const val NETWORK_RETRY_LIMIT = 20
        private const val NETWORK_RETRY_DELAY_SECONDS = 3L
        @Volatile private var liveInstance: CehoVpnService? = null
        fun requestNodeExitCountry(nodeRaw: String, callback: (String?, String?) -> Unit): Boolean =
            liveInstance?.requestExitCountry(nodeRaw, callback) ?: false
        @Volatile private var liveTunnelConnected = false
        @Volatile private var liveServiceInstance = false
        fun hasLiveTunnel(): Boolean = liveTunnelConnected
        fun hasLiveServiceInstance(): Boolean = liveServiceInstance
    }

    private val failoverExecutor = Executors.newSingleThreadScheduledExecutor { task ->
        Thread(task, "ceho-failover").apply { isDaemon = true }
    }
    // Native selector calls have no timeout; a hang here used to freeze the whole failover loop.
    private val nativeCalls = BoundedCall("ceho-native")
    @Volatile private var failoverTask: ScheduledFuture<*>? = null
    @Volatile private var probeConnection: HttpURLConnection? = null
    @Volatile private var activeProbePort = 0
    @Volatile private var activeProfile: ClientProfile? = null
    @Volatile private var activeNodeName: String? = null
    @Volatile private var activeSubscriptionText: String? = null
    @Volatile private var activeGeneration = 0L
    @Volatile private var readyGeneration = -1L
    @Volatile private var lastPhysicalNetwork: Network? = null
    private val startGeneration = StartGeneration()
    private val tunnelLock = Any()
    private val lifecycleExecutor = Executors.newSingleThreadExecutor { task ->
        Thread(task, "cehoproxy-lifecycle").apply { isDaemon = true }
    }
    @Volatile private var latestStartId = 0
    @Volatile private var lastAutoRestartMillis = 0L
    @Volatile private var xrayProcess: Process? = null
    @Volatile private var destroyed = false
    private var acceptTun = false
    private var networkRetriesLeft = 0
    private var commandServer: CommandServer? = null
    private var tunnel: ParcelFileDescriptor? = null
    @Volatile private var physicalNetwork: Network? = null
    private val interfaceMonitors = ConcurrentHashMap<InterfaceUpdateListener, ConnectivityManager.NetworkCallback>()
    private val interfaceMonitorThreads = ConcurrentHashMap<InterfaceUpdateListener, HandlerThread>()

    override fun onCreate() {
        super.onCreate()
        liveServiceInstance = true
        liveInstance = this
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        latestStartId = startId
        if (intent?.action == ACTION_STOP) {
            stopTunnel(startId)
            return START_NOT_STICKY
        }
        createChannel()
        activate(null)
        val notification = notification("Подключаемся")
        if (Build.VERSION.SDK_INT >= 34) startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        else startForeground(NOTIFICATION_ID, notification)
        requestStart(startId)
        return START_STICKY
    }

    private fun requestStart(startId: Int) {
        if (destroyed) return
        val generation = startGeneration.beginStart()
        cancelFailover()
        publishStatus("starting", null)
        runCatching { failoverExecutor.schedule({ onConnectTimeout(generation) }, 15, TimeUnit.SECONDS) }
        try {
            lifecycleExecutor.execute { startTunnel(generation, startId) }
        } catch (_: RejectedExecutionException) {
            if (startGeneration.isCurrent(generation)) publishStatus("error", "Служба VPN уже завершает работу")
        }
    }

    private fun startTunnel(generation: Long, startId: Int) {
        try {
            if (!startGeneration.isCurrent(generation)) return
            closeNativeResources()
            if (!startGeneration.isCurrent(generation)) return
            val profile = ProfileStore(this).read()
            if (!hasInternet()) error("Нет интернета. Подключитесь к Wi-Fi или мобильной сети")
            if (SubscriptionExpiry.allExpired(profile.nodes, System.currentTimeMillis() / 1000)) error("Подписка истекла. Обновите её у провайдера")
            AppScopePolicy.validate(profile.apps, profile.apps, profile.excludeSelectedApps)?.let { error(it) }
            val selected = profile.selectedNode
            val xrayPorts = if (profile.importedConfig == null) {
                startXray(RoutingConfig.allowedNodesFor(profile.nodes, profile.excludedCountrySets, profile.disabledSubscriptions.toSet()).filter(XrayBridge::needsXray))
            } else emptyMap()
            val policy = RoutePolicy(
                apps = profile.apps,
                sites = profile.sites,
                localDpiApps = ru.cebers.cehoproxy.core.LocalDpiPolicy.activeApps(profile),
                countryRuleSets = if (profile.importedConfig == null) CountryRuleSetCache.prepare(this, profile.directCountryCode) else emptyList(),
                siteMode = if (profile.onlySites) SiteMode.ONLY_LIST else SiteMode.ALL_EXCEPT_LIST,
                excludeSelectedApps = profile.excludeSelectedApps,
                appNodeRoutes = profile.appNodeRoutes,
                siteNodeRoutes = profile.siteNodeRoutes,
                autoRotationEnabled = profile.autoRotationEnabled,
                autoRotationIntervalSeconds = profile.autoRotationIntervalSeconds,
                autoExcludedRouteIds = profile.autoExcludedRouteIds.toSet(),
                excludedCountriesBySubscription = profile.excludedCountrySets,
                blockYoutubeAds = profile.blockYoutubeAds,
                disabledSubscriptions = profile.disabledSubscriptions.toSet(),
                xrayPorts = xrayPorts,
            )
            if (profile.importedConfig == null && profile.nodes.isNotEmpty() && RoutingConfig.allowedNodesFor(profile.nodes, policy.excludedCountriesBySubscription, policy.disabledSubscriptions).isEmpty()) error("Все подписки отключены. Включите хотя бы одну в «Серверы»")
            val routingConfig = profile.importedConfig?.let { RoutingConfig.applyToImported(it, policy) }
                ?: if (profile.nodes.isNotEmpty() || policy.localDpiApps.isNotEmpty()) RoutingConfig.build(profile.nodes, profile.selected, policy) else selected?.let { RoutingConfig.build(it, policy) }
                ?: error("Профиль пуст")
            val candidates = OrderedFailoverConfig.candidates(routingConfig)
            val probePort = if (candidates.isNotEmpty()) ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")).use { it.localPort } else 0
            val config = AndroidSystemTrustBundle.applyTo(this,
                if (probePort != 0) OrderedFailoverConfig.withProbe(routingConfig, probePort) else routingConfig)
            if (!startGeneration.isCurrent(generation)) return
            val server = CommandServer(this, this)
            commandServer = server
            synchronized(tunnelLock) { acceptTun = true }
            server.start()
            server.startOrReloadService(config, OverrideOptions())
            readyGeneration = generation
            networkRetriesLeft = 0
            val checkNode = candidates.isNotEmpty() && profile.importedConfig == null
            val published = startGeneration.runIfCurrent(generation) {
                if (!checkNode) {
                    activate(profile.selectedNode)
                    publishStatus("connected", null)
                    getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, connectedNotification())
                }
            }
            if (published) {
                getSharedPreferences(PREFS, MODE_PRIVATE).edit().putInt("probe_port", probePort).apply()
                activeProbePort = probePort
                activeProfile = profile
                activeGeneration = generation
                if (checkNode && profile.autoRotationEnabled) startFailover(generation, profile, candidates, probePort)
                else if (checkNode) checkSelectedNode(generation, profile, candidates, probePort)
            }
            if (!published) {
                closeNativeResources()
                return
            }
        } catch (error: Throwable) {
            closeNativeResources()
            if (retryAfterNetworkFailure(generation)) return
            startGeneration.runIfCurrent(generation) {
                publishStatus("error", error.message ?: error.javaClass.simpleName)
                stopForeground(STOP_FOREGROUND_REMOVE)
                stopSelfResult(startId)
            }
        }
    }

    /** Новая сеть часто ещё без интернета: пробуем снова, а не гасим службу. */
    private fun retryAfterNetworkFailure(generation: Long): Boolean {
        if (destroyed || networkRetriesLeft <= 0 || hasInternet() || !startGeneration.isCurrent(generation)) return false
        networkRetriesLeft--
        EventLog.append(this, "сеть ещё не готова, повторяем подключение")
        publishStatus("starting", null)
        runCatching { failoverExecutor.schedule({ requestStart(latestStartId) }, NETWORK_RETRY_DELAY_SECONDS, TimeUnit.SECONDS) }
        return true
    }

    private fun stopTunnel(startId: Int = latestStartId) {
        startGeneration.invalidate()
        cancelFailover()
        publishStatus("stopped", null)
        stopForeground(STOP_FOREGROUND_REMOVE)
        runCatching { lifecycleExecutor.execute { closeNativeResources() } }
        stopSelfResult(startId)
    }

    private fun requestExitCountry(nodeRaw: String, callback: (String?, String?) -> Unit): Boolean {
        val profile = activeProfile ?: return false
        val generation = activeGeneration
        val tag = tagsByRaw(profile)[nodeRaw] ?: return false
        val port = activeProbePort.takeIf { it > 0 } ?: return false
        return try {
            failoverExecutor.execute {
                val result = if (startGeneration.isCurrent(generation)) runCatching {
                    Libbox.newStandaloneCommandClient().selectOutbound(OrderedFailoverConfig.PROBE_TAG, tag)
                    NodeExitCountryProbe.fetch(port)
                }.getOrNull() else null
                if (startGeneration.isCurrent(generation)) callback(result?.countryCode, result?.exitIp)
                else callback(null, null)
            }
            true
        } catch (_: RejectedExecutionException) { false }
    }

    private fun tagsByRaw(profile: ClientProfile): Map<String, String> =
        RoutingConfig.tagsByRaw(profile.nodes, profile.excludedCountrySets, profile.disabledSubscriptions.toSet())

    private fun startOrder(candidates: List<String>, profile: ClientProfile): List<String> {
        val index = candidates.indexOf(tagsByRaw(profile)[profile.selectedNode?.raw])
        if (index <= 0 || index >= candidates.size) return candidates
        return candidates.drop(index) + candidates.take(index)
    }

    private fun onConnectTimeout(generation: Long) {
        if (readyGeneration == generation || !startGeneration.isCurrent(generation)) return
        if (getSharedPreferences(PREFS, MODE_PRIVATE).getString("status", null) != "starting") return
        startGeneration.invalidate()
        cancelFailover()
        publishStatus("error", "Не удалось подключиться за 15 секунд. Проверьте интернет и попробуйте снова")
        stopForeground(STOP_FOREGROUND_REMOVE)
        runCatching { lifecycleExecutor.execute { closeNativeResources() } }
        stopSelf()
    }

    private fun hasInternet(): Boolean {
        val connectivity = getSystemService(ConnectivityManager::class.java)
        val capabilities = connectivity.activeNetwork?.let(connectivity::getNetworkCapabilities) ?: return false
        return capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    }

    private fun scheduleReconnect() {
        if (destroyed) return
        runCatching { failoverExecutor.schedule({ reconnectAfterNetworkChange() }, 2, TimeUnit.SECONDS) }
    }

    private fun reconnectAfterNetworkChange() {
        val status = getSharedPreferences(PREFS, MODE_PRIVATE).getString("status", "stopped")
        if (destroyed || status == "stopped") return
        EventLog.append(this, "сеть сменилась, переподключаемся")
        networkRetriesLeft = NETWORK_RETRY_LIMIT
        requestStart(latestStartId)
    }

    private fun restartIfStuck(generation: Long, failedChecks: Int) {
        if (destroyed || !startGeneration.isCurrent(generation)) return
        if (!StuckTunnelPolicy.shouldRestart(failedChecks, System.currentTimeMillis(), lastAutoRestartMillis) || !hasInternet()) return
        lastAutoRestartMillis = System.currentTimeMillis()
        EventLog.append(this, "серверы не отвечают, перезапускаем туннель автоматически")
        requestStart(latestStartId)
    }

    private fun cancelFailover() {
        activeProfile = null
        activeProbePort = 0
        failoverTask?.cancel(true)
        failoverTask = null
        probeConnection?.disconnect()
        probeConnection = null
    }

    private fun checkSelectedNode(generation: Long, profile: ClientProfile, candidates: List<String>, port: Int) {
        val tag = tagsByRaw(profile)[profile.selectedNode?.raw]
        failoverExecutor.execute {
            val healthy = tag != null && nativeCalls.run(SELECT_TIMEOUT_MILLIS) {
                Libbox.newStandaloneCommandClient().selectOutbound(OrderedFailoverConfig.PROBE_TAG, tag)
                probe(port, generation)
            } == true
            startGeneration.runIfCurrent(generation) {
                if (healthy) {
                    activate(profile.selectedNode)
                    publishStatus("connected", null)
                    getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, connectedNotification())
                } else {
                    publishStatus("error", "Выбранный узел не отвечает. Смените узел или включите автовыбор")
                    getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, notification("Узел не отвечает"))
                }
            }
            if (tag != null) startManualWatch(generation, tag, port, healthy)
        }
    }

    private fun startManualWatch(generation: Long, tag: String, port: Int, initiallyHealthy: Boolean) {
        var up = initiallyHealthy
        var failures = 0
        val task = failoverExecutor.scheduleWithFixedDelay({
            if (!startGeneration.isCurrent(generation)) return@scheduleWithFixedDelay
            val ok = nativeCalls.run(SELECT_TIMEOUT_MILLIS) {
                Libbox.newStandaloneCommandClient().selectOutbound(OrderedFailoverConfig.PROBE_TAG, tag)
                probe(port, generation)
            } == true
            failures = if (ok) 0 else failures + 1
            startGeneration.runIfCurrent(generation) {
                if (ok && !up) {
                    up = true
                    publishStatus("connected", null)
                    getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, connectedNotification())
                } else if (!ok && up && failures >= 2) {
                    up = false
                    publishStatus("error", "Выбранный узел перестал отвечать. Смените узел или включите автовыбор")
                    getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, notification("Узел не отвечает"))
                }
            }
            restartIfStuck(generation, failures)
        }, 60, 60, TimeUnit.SECONDS)
        if (startGeneration.isCurrent(generation)) failoverTask = task else task.cancel(true)
    }

    private fun startFailover(generation: Long, profile: ClientProfile, candidates: List<String>, port: Int) {
        var failedChecks = 0
        val task = failoverExecutor.scheduleWithFixedDelay({
            if (!startGeneration.isCurrent(generation)) return@scheduleWithFixedDelay
            try {
                val client = Libbox.newStandaloneCommandClient()
                // Separate selector: a failed test must not change assigned application/site routes.
                val healthy = OrderedFailover.firstHealthy(startOrder(candidates, profile), { startGeneration.isCurrent(generation) }) { tag ->
                    nativeCalls.run(SELECT_TIMEOUT_MILLIS) {
                        client.selectOutbound(OrderedFailoverConfig.PROBE_TAG, tag)
                        probe(port, generation)
                    } == true
                }
                startGeneration.runIfCurrent(generation) {
                    nativeCalls.run(SELECT_TIMEOUT_MILLIS) { client.selectOutbound(OrderedFailoverConfig.GLOBAL_TAG, healthy ?: "block") }

                    if (healthy != null) {
                        failedChecks = 0
                        val healthyRaw = tagsByRaw(profile).entries.firstOrNull { it.value == healthy }?.key
                        val activeNode = profile.nodes.firstOrNull { it.raw == healthyRaw }
                        if (activeNode != null) {
                            activate(activeNode)
                            val store = ProfileStore(this)
                            store.update { current ->
                                FailoverSelection.apply(current, profile, activeNode.raw, startGeneration.isCurrent(generation))
                            }
                        }
                        publishStatus("connected", null)
                        getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, connectedNotification())
                    } else {
                        failedChecks++
                        // Keep TUN alive and reject global traffic until the next bounded check.
                        publishStatus("error", if (hasInternet()) "Серверы не отвечают. Проверка повторится автоматически" else "Нет интернета. Проверка повторится автоматически")
                        getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, notification("Нет доступных узлов"))
                    }
                }
                restartIfStuck(generation, failedChecks)
            } catch (_: Exception) {
                failedChecks++
                startGeneration.runIfCurrent(generation) { publishStatus("error", "Не удалось проверить узлы. Проверка повторится автоматически") }
                restartIfStuck(generation, failedChecks)
            }
        }, 0, 30, TimeUnit.SECONDS)
        if (startGeneration.isCurrent(generation)) failoverTask = task else task.cancel(true)
    }

    private fun probe(port: Int, generation: Long): Boolean {
        if (!startGeneration.isCurrent(generation)) return false
        val connection = URL("https://1.1.1.1/cdn-cgi/trace").openConnection(
            Proxy(Proxy.Type.HTTP, InetSocketAddress("127.0.0.1", port))) as HttpURLConnection
        connection.connectTimeout = 4000
        connection.readTimeout = 4000
        connection.instanceFollowRedirects = false
        connection.useCaches = false
        connection.setRequestProperty("Connection", "close")
        probeConnection = connection
        return try { connection.responseCode in 200..399 && startGeneration.isCurrent(generation) }
        finally { connection.disconnect(); if (probeConnection === connection) probeConnection = null }
    }

    private fun startXray(nodes: List<ProxyNode>): Map<String, Int> {
        if (nodes.isEmpty()) return emptyMap()
        val ports = nodes.associate { node -> node.routeId to ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")).use { it.localPort } }
        return runCatching {
            val configFile = File(filesDir, "xray.json").apply { writeText(XrayBridge.config(nodes, ports)) }
            val binary = File(applicationInfo.nativeLibraryDir, "libxray.so")
            val process = ProcessBuilder(binary.path, "run", "-config", configFile.path)
                .redirectErrorStream(true)
                .redirectOutput(File(filesDir, "xray.log"))
                .start()
            check(!process.waitFor(500, TimeUnit.MILLISECONDS)) { "процесс завершился сразу" }
            xrayProcess = process
            ports
        }.getOrElse { error ->
            EventLog.append(this, "Xray не запустился, узлы xhttp и mKCP недоступны: ${error.message}")
            emptyMap()
        }
    }

    private fun closeNativeResources() {
        synchronized(tunnelLock) { acceptTun = false }
        xrayProcess?.destroy()
        xrayProcess = null
        val server = commandServer
        commandServer = null
        runCatching { server?.closeService() }
        runCatching { server?.close() }
        val oldTunnel = synchronized(tunnelLock) {
            tunnel.also { tunnel = null }
        }
        runCatching { oldTunnel?.close() }
    }

    override fun onRevoke() {
        startGeneration.invalidate()
        cancelFailover()
        publishStatus("stopped", "Другое VPN отключило CehoProxy. Нажмите на водоворот, чтобы включить снова")
        stopForeground(STOP_FOREGROUND_REMOVE)
        runCatching { lifecycleExecutor.execute { closeNativeResources() } }
        stopSelf()
    }
    override fun onDestroy() {
        destroyed = true
        startGeneration.invalidate()
        cancelFailover()
        failoverExecutor.shutdownNow()
        nativeCalls.shutdown()
        if (liveTunnelConnected) publishStatus("stopped", null)
        else liveTunnelConnected = false
        liveServiceInstance = false
        liveInstance = null
        runCatching { lifecycleExecutor.execute { closeNativeResources() } }
        lifecycleExecutor.shutdown()
        super.onDestroy()
    }

    override fun openTun(options: TunOptions): Int {
        check(VpnService.prepare(this) == null) { "Разрешение Android VPN не выдано" }
        val builder = Builder().setSession("CehoProxy").setMtu(options.mtu)
        val addresses4 = options.inet4Address
        while (addresses4.hasNext()) { val address = addresses4.next(); builder.addAddress(address.address(), address.prefix()) }
        val addresses6 = options.inet6Address
        while (addresses6.hasNext()) { val address = addresses6.next(); builder.addAddress(address.address(), address.prefix()) }
        val dns = options.dnsServerAddress
        while (dns.hasNext()) builder.addDnsServer(dns.next())
        if (Build.VERSION.SDK_INT >= 33) {
            val routes4 = options.inet4RouteAddress
            if (routes4.hasNext()) while (routes4.hasNext()) builder.addRoute(routes4.next().toIpPrefix()) else builder.addRoute("0.0.0.0", 0)
            val routes6 = options.inet6RouteAddress
            if (routes6.hasNext()) while (routes6.hasNext()) builder.addRoute(routes6.next().toIpPrefix())
            val excludes4 = options.inet4RouteExcludeAddress
            while (excludes4.hasNext()) builder.excludeRoute(excludes4.next().toIpPrefix())
            val excludes6 = options.inet6RouteExcludeAddress
            while (excludes6.hasNext()) builder.excludeRoute(excludes6.next().toIpPrefix())
        } else {
            val routes4 = options.inet4RouteRange
            if (routes4.hasNext()) while (routes4.hasNext()) { val route = routes4.next(); builder.addRoute(route.address(), route.prefix()) } else builder.addRoute("0.0.0.0", 0)
            val routes6 = options.inet6RouteRange
            while (routes6.hasNext()) { val route = routes6.next(); builder.addRoute(route.address(), route.prefix()) }
        }
        val include = options.includePackage
        val includedPackages = buildList { while (include.hasNext()) add(include.next()) }
        val exclude = options.excludePackage
        val excludedPackages = buildList { while (exclude.hasNext()) add(exclude.next()) }
        val storedProfile = ProfileStore(this).read()
        val configuredPackages = if (storedProfile.excludeSelectedApps) excludedPackages else includedPackages
        AppScopePolicy.validate(storedProfile.apps, configuredPackages, storedProfile.excludeSelectedApps)?.let { error(it) }
        // Xray runs in this app's process; without this its outbound sockets would loop back into the tunnel.
        if (includedPackages.isEmpty()) builder.addDisallowedApplication(this.packageName)
        includedPackages.forEach { packageName ->
            try {
                builder.addAllowedApplication(packageName)
            } catch (error: Exception) {
                throw IllegalStateException("Не удалось ограничить VPN приложением $packageName", error)
            }
        }
        excludedPackages.forEach { packageName ->
            try {
                builder.addDisallowedApplication(packageName)
            } catch (error: Exception) {
                throw IllegalStateException("Не удалось применить исключение для приложения $packageName", error)
            }
        }
        val (established, oldTunnel) = synchronized(tunnelLock) {
            check(acceptTun && !destroyed) { "VPN останавливается; повторите подключение" }
            val current = builder.establish() ?: error("Android не создал VPN-интерфейс")
            current to tunnel.also { tunnel = current }
        }
        runCatching { oldTunnel?.close() }
        return established.fd
    }

    override fun usePlatformAutoDetectInterfaceControl() = true
    override fun autoDetectInterfaceControl(fd: Int) { check(protect(fd)) { "Не удалось исключить соединение ядра из VPN" } }
    override fun useProcFS() = false
    override fun findConnectionOwner(ipProtocol: Int, sourceAddress: String, sourcePort: Int, destinationAddress: String, destinationPort: Int): ConnectionOwner {
        check(Build.VERSION.SDK_INT >= 29) { "Android этой версии не сообщает владельца соединения" }
        val uid = getSystemService(android.net.ConnectivityManager::class.java).getConnectionOwnerUid(ipProtocol, java.net.InetSocketAddress(sourceAddress, sourcePort), java.net.InetSocketAddress(destinationAddress, destinationPort))
        val owner = ConnectionOwner().apply { userId = uid; userName = packageManager.getPackagesForUid(uid)?.firstOrNull() ?: "" }
        owner.setAndroidPackageNames(StringList(packageManager.getPackagesForUid(uid)?.toList().orEmpty()))
        return owner
    }
    @SuppressLint("NewApi")
    override fun startDefaultInterfaceMonitor(listener: InterfaceUpdateListener) {
        closeDefaultInterfaceMonitor(listener)
        val connectivity = getSystemService(ConnectivityManager::class.java)
        val callbackThread = HandlerThread("ceho-network-monitor").apply { start() }
        val callbackHandler = Handler(callbackThread.looper)
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                val previous = lastPhysicalNetwork
                lastPhysicalNetwork = network
                physicalNetwork = network
                updateDefaultInterface(listener, network)
                if (previous != null && previous != network) scheduleReconnect()
            }
            override fun onLinkPropertiesChanged(network: Network, linkProperties: LinkProperties) {
                if (physicalNetwork == network) updateDefaultInterface(listener, network, linkProperties)
            }
            override fun onLost(network: Network) {
                if (physicalNetwork == network) {
                    physicalNetwork = null
                    listener.updateDefaultInterface("", -1, false, false)
                }
            }
        }
        interfaceMonitors[listener] = callback
        interfaceMonitorThreads[listener] = callbackThread
        try {
            when (NetworkMonitorStrategy.forApi(Build.VERSION.SDK_INT)) {
                NetworkMonitorStrategy.BEST_MATCHING -> registerBestMatchingNetwork(connectivity, callback, callbackHandler)
                NetworkMonitorStrategy.REQUEST -> connectivity.requestNetwork(physicalNetworkRequest(), callback, callbackHandler)
                NetworkMonitorStrategy.DEFAULT_WITH_HANDLER -> connectivity.registerDefaultNetworkCallback(callback, callbackHandler)
                NetworkMonitorStrategy.DEFAULT -> connectivity.registerDefaultNetworkCallback(callback)
            }
        } catch (error: Throwable) {
            interfaceMonitors.remove(listener, callback)
            interfaceMonitorThreads.remove(listener, callbackThread)
            callbackThread.quitSafely()
            throw error
        }
    }
    override fun closeDefaultInterfaceMonitor(listener: InterfaceUpdateListener) {
        val callback = interfaceMonitors.remove(listener) ?: return
        runCatching { getSystemService(ConnectivityManager::class.java).unregisterNetworkCallback(callback) }
        interfaceMonitorThreads.remove(listener)?.quitSafely()
    }
    override fun getInterfaces(): NetworkInterfaceIterator = InterfaceList(NetworkInterface.getNetworkInterfaces()?.toList().orEmpty())
    override fun underNetworkExtension() = false
    override fun includeAllNetworks() = false
    override fun readWIFIState(): WIFIState? = null
    override fun clearDNSCache() = Unit
    override fun localDNSTransport(): LocalDNSTransport = AndroidLocalDnsTransport { physicalNetwork }
    override fun sendNotification(notification: io.nekohasekai.libbox.Notification) = Unit
    override fun cancelNotification(identifier: String, typeID: Int) = Unit
    override fun startNeighborMonitor(listener: NeighborUpdateListener) = Unit
    override fun closeNeighborMonitor(listener: NeighborUpdateListener) = Unit
    override fun registerMyInterface(name: String) = Unit
    override fun usePlatformShell() = false
    override fun checkPlatformShell() { error("Системная оболочка недоступна") }
    override fun openShellSession(user: PlatformUser, command: String, environ: StringIterator, term: String, rows: Int, cols: Int): ShellSession = error("Системная оболочка недоступна")
    override fun lookupUser(username: String): PlatformUser = error("Пользователи системы недоступны")
    override fun lookupSFTPServer(): String = error("SFTP недоступен")
    override fun readSystemSSHHostKey(): String = error("SSH недоступен")
    override fun tailscaleHostname(): String = ""
    override fun usePlatformBridge() = false
    override fun createBridge(options: BridgeOptions): BridgeSession = error("Bridge требует root")
    override fun serviceStop() { stopTunnel() }
    override fun serviceReload() { requestStart(latestStartId) }
    override fun getSystemProxyStatus(): SystemProxyStatus = SystemProxyStatus().apply { available = false; enabled = false }
    override fun setSystemProxyEnabled(enabled: Boolean) = Unit
    override fun triggerNativeCrash() = Unit
    override fun writeDebugMessage(message: String) { android.util.Log.d("CehoProxy", message) }
    override fun connectSSHAgent() = -1

    private fun createChannel() {
        if (Build.VERSION.SDK_INT >= 26) getSystemService(NotificationManager::class.java).createNotificationChannel(NotificationChannel(CHANNEL, "CehoProxy VPN", NotificationManager.IMPORTANCE_LOW))
    }

    @androidx.annotation.RequiresApi(31)
    private fun registerBestMatchingNetwork(connectivity: ConnectivityManager, callback: ConnectivityManager.NetworkCallback, handler: Handler) {
        connectivity.registerBestMatchingNetworkCallback(physicalNetworkRequest(), callback, handler)
    }

    private fun physicalNetworkRequest(): NetworkRequest = NetworkRequest.Builder()
        .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
        .addCapability(NetworkCapabilities.NET_CAPABILITY_NOT_RESTRICTED)
        .build()
    private fun nodeLabel(node: ProxyNode): String {
        val name = NodePresentation.routeName(node)
        val code = NodeCountryCache(this).read(node.routeId)?.countryCode ?: NodeCountry.resolve(node.name)
        val flag = code?.let { NodePresentation.flag(it) }?.takeUnless { name.startsWith(it) }?.let { "$it " }.orEmpty()
        return flag + name
    }

    private fun activate(node: ProxyNode?) {
        activeNodeName = node?.let(::nodeLabel)
        activeSubscriptionText = node?.let(NodePresentation::subscriptionSource)
    }

    private fun connectedNotification(): Notification {
        val since = getSharedPreferences(PREFS, MODE_PRIVATE).getLong("connected_at", System.currentTimeMillis())
        return notification(activeNodeName?.let { "Через: $it" } ?: "Подключено", activeSubscriptionText, since)
    }

    private fun notification(text: String, subscription: String? = null, sinceMillis: Long? = null): Notification = NotificationCompat.Builder(this, CHANNEL).setSmallIcon(ru.cebers.cehoproxy.R.drawable.ic_ceho_whirlpool).setContentTitle("CehoProxy").setContentText(text).setSubText(subscription).setOngoing(true).setCategory(NotificationCompat.CATEGORY_SERVICE)
        .setContentIntent(PendingIntent.getActivity(this, 2, Intent(this, ru.cebers.cehoproxy.MainActivity::class.java), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE))
        .apply { if (sinceMillis != null) setWhen(sinceMillis).setShowWhen(true).setUsesChronometer(true) }
        .addAction(0, "Выключить", PendingIntent.getService(this, 1, Intent(this, CehoVpnService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT))
        .build()
    private fun publishStatus(status: String, message: String?) {
        liveTunnelConnected = status == "connected"
        val prefs = getSharedPreferences(PREFS, MODE_PRIVATE)
        val editor = prefs.edit().putString("status", status).putString("message", message)
        if (status == "connected" && prefs.getString("status", null) != "connected") editor.putLong("connected_at", System.currentTimeMillis())
        if (status != "connected") editor.remove("probe_port").remove("connected_at")
        editor.apply()
        EventLog.append(this, "статус=$status" + (message?.let { ": $it" }.orEmpty()))
        CehoWidgetProvider.refreshAll(this)
        android.service.quicksettings.TileService.requestListeningState(this, android.content.ComponentName(this, CehoTileService::class.java))
        sendBroadcast(Intent(ACTION_STATUS).setPackage(packageName).putExtra("status", status).putExtra("message", message))
    }
}

private fun CehoVpnService.updateDefaultInterface(listener: InterfaceUpdateListener, network: Network, properties: LinkProperties? = null) {
    val name = (properties ?: getSystemService(ConnectivityManager::class.java).getLinkProperties(network))?.interfaceName
    val index = name?.let { runCatching { NetworkInterface.getByName(it)?.index }.getOrNull() }
    if (name != null && index != null) listener.updateDefaultInterface(name, index, false, false)
}

@SuppressLint("NewApi")
private fun io.nekohasekai.libbox.RoutePrefix.toIpPrefix(): IpPrefix = IpPrefix(InetAddress.getByName(address()), prefix())
private fun java.net.NetworkInterface.toBoxInterface() = io.nekohasekai.libbox.NetworkInterface().apply {
    name = this@toBoxInterface.name
    index = this@toBoxInterface.index
    mtu = runCatching { this@toBoxInterface.mtu }.getOrDefault(1500)
    flags = runCatching {
        (if (this@toBoxInterface.isUp) 0x1 else 0) or
            (if (this@toBoxInterface.supportsMulticast()) 0x1000 else 0) or
            (if (this@toBoxInterface.isLoopback) 0x8 else 0) or
            (if (this@toBoxInterface.isPointToPoint) 0x10 else 0)
    }.getOrDefault(0)
    type = InterfaceTypePolicy.forName(name)
    addresses = StringList(this@toBoxInterface.interfaceAddresses.map {
        "${it.address.hostAddress.orEmpty().substringBefore('%')}/${it.networkPrefixLength}"
    })
}
private class StringList(private val values: List<String>) : StringIterator {
    private var index = 0
    override fun len() = values.size
    override fun hasNext() = index < values.size
    override fun next() = values[index++]
}
private class InterfaceList(values: List<NetworkInterface>) : NetworkInterfaceIterator {
    private val iterator = values.map { it.toBoxInterface() }.iterator()
    override fun hasNext() = iterator.hasNext()
    override fun next() = iterator.next()
}
