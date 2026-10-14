package ru.cebers.cehoproxy

import androidx.test.platform.app.InstrumentationRegistry
import io.nekohasekai.libbox.Libbox
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test
import ru.cebers.cehoproxy.core.NodeShareParser
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.ProfileCodec
import ru.cebers.cehoproxy.core.RoutePolicy
import ru.cebers.cehoproxy.core.RoutingConfig
import ru.cebers.cehoproxy.core.SubscriptionFetcher
import ru.cebers.cehoproxy.core.SubscriptionRequestOptions

class LiveSubscriptionSmokeTest {
    @Test fun privateOperatorInputsHaveLiveOutboundEvidence() {
        val arguments = InstrumentationRegistry.getArguments()
        val filename = arguments.getString("operatorInputFile")
        assumeTrue("Private operator input file must be supplied", !filename.isNullOrBlank())
        require(filename!!.matches(Regex("[A-Za-z0-9_.-]+")))
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val resultName = arguments.getString("operatorResultFile") ?: "ceho-live-results.json"
        require(resultName.matches(Regex("[A-Za-z0-9_.-]+")))
        val targetHost = arguments.getString("targetHost") ?: "api.ipify.org"
        require(targetHost in setOf("api.ipify.org", "checkip.amazonaws.com"))
        val nodesName = arguments.getString("operatorNodesFile") ?: "ceho-live-nodes.json"
        require(nodesName.matches(Regex("[A-Za-z0-9_.-]+")))
        val operatorNodes = org.json.JSONArray()
        val nodesFile = java.io.File(context.filesDir, nodesName)
        val nativeErrorFile = java.io.File(context.filesDir, "ceho-live-native-errors.log")
        nativeErrorFile.writeText("")
        val inputs = org.json.JSONObject(java.io.File(context.filesDir, filename).readText()).getJSONArray("inputs")
        val results = org.json.JSONArray()
        val connectivity = context.getSystemService(android.net.ConnectivityManager::class.java)
        val physical = connectivity.allNetworks.firstOrNull { network ->
            connectivity.getNetworkCapabilities(network)?.let {
                it.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
                    it.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_NOT_VPN) &&
                    it.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_VALIDATED)
            } == true
        }
        check(physical != null) { "Validated physical network unavailable" }
        var failures = 0
        val reportFile = java.io.File(context.filesDir, resultName)
        fun persist() { reportFile.writeText(org.json.JSONObject().put("targetHost", targetHost)
            .put("nativeSocketsPhysicallyBound", true)
            .put("physicalInterface", connectivity.getLinkProperties(physical)?.interfaceName)
            .put("physicalIsWifi", connectivity.getNetworkCapabilities(physical)?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI) == true)
            .put("results", results).toString(2)) }
        for (inputIndex in 0 until inputs.length()) {
            val result = org.json.JSONObject().put("inputIndex", inputIndex)
            results.put(result)
            var stage = "classify"
            try {
                val entry = inputs.getJSONObject(inputIndex)
                val value = entry.getString("value")
                val kind = ru.cebers.cehoproxy.core.SubscriptionInput.classify(value)
                result.put("kind", kind.name)
                    .put("remoteFetchPerformed", kind == ru.cebers.cehoproxy.core.SubscriptionInputKind.REMOTE_SUBSCRIPTION)
                stage = "fetch"
                val fetchStarted = System.nanoTime()
                val fetched = if (kind == ru.cebers.cehoproxy.core.SubscriptionInputKind.REMOTE_SUBSCRIPTION)
                    SubscriptionFetcher.fetchWithMetadata(value, SubscriptionRequestOptions()) else null
                val body = fetched?.body ?: value
                result.put("fetchMs", (System.nanoTime() - fetchStarted) / 1_000_000)
                    .put("fetchBytes", body.toByteArray(Charsets.UTF_8).size)
                stage = "parse"
                val nodes = fetched?.parseNodes() ?: ru.cebers.cehoproxy.core.SubscriptionMetadata.fromBody(body).attachTo(NodeShareParser.parse(body))
                check(nodes.isNotEmpty())
                result.put("parsedNodes", nodes.size)
                operatorNodes.put(org.json.JSONObject().put("inputIndex", inputIndex)
                    .put("profile", ProfileCodec.encode(ClientProfile(nodes = nodes))))
                nodesFile.writeText(org.json.JSONObject().put("inputs", operatorNodes).toString())
                val checks = org.json.JSONArray()
                result.put("nodes", checks)
                require(nodes.size <= 500) { "Operator review required for large batch" }
                result.put("typeCounts", org.json.JSONObject(nodes.groupingBy { it.type }.eachCount()))
                for ((nodeIndex, node) in nodes.withIndex()) {
                    val nodeResult = org.json.JSONObject().put("nodeIndex", nodeIndex).put("type", node.type)
                    checks.put(nodeResult)
                    var nodeStage = "config"
                    val nodeStarted = System.nanoTime()
                    try {
                        val generated = ru.cebers.cehoproxy.vpn.AndroidSystemTrustBundle.applyTo(context,
                            RoutingConfig.build(node, RoutePolicy()))
                        Libbox.checkConfig(generated)
                        nodeResult.put("configAccepted", true)
                        val tcpStarted = System.nanoTime()
                        try {
                            java.net.Socket().use { direct ->
                                physical.bindSocket(direct)
                                direct.connect(java.net.InetSocketAddress(node.server, node.port), 6_000)
                                nodeResult.put("directTcpConnected", true)
                            }
                        } catch (error: Throwable) {
                            nodeResult.put("directTcpConnected", false).put("directTcpErrorClass", error.javaClass.simpleName)
                        } finally {
                            nodeResult.put("directTcpMs", (System.nanoTime() - tcpStarted) / 1_000_000)
                        }
                        nativeErrorFile.appendText("inputIndex=$inputIndex nodeIndex=$nodeIndex\n")
                        nodeResult.put("exitIp", probeNativeOutbound(generated, connectivity, physical,
                            targetHost, nativeErrorFile) { nodeStage = it })
                        nodeResult.put("httpsSucceeded", true)
                    } catch (error: Throwable) {
                        failures++
                        nodeResult.put("failureStage", nodeStage).put("errorClass", error.javaClass.simpleName)
                    } finally {
                        nodeResult.put("durationMs", (System.nanoTime() - nodeStarted) / 1_000_000)
                        persist()
                    }
                }
                result.put("accountedNodes", checks.length())
                    .put("configAcceptedNodes", (0 until checks.length()).count { checks.getJSONObject(it).optBoolean("configAccepted") })
                    .put("httpsSucceededNodes", (0 until checks.length()).count { checks.getJSONObject(it).optBoolean("httpsSucceeded") })
                check(checks.length() == nodes.size)
                result.put("complete", true)
            } catch (error: Throwable) {
                failures++
                result.put("failureStage", stage).put("errorClass", error.javaClass.simpleName)
            } finally { persist() }
        }
        assertEquals("Live checks failed; inspect sanitized private report", 0, failures)
    }

    @Test fun privateSnapshotComparisonConfigs() {
        val arguments = InstrumentationRegistry.getArguments()
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val snapshotName = arguments.getString("operatorSnapshotFile") ?: "ceho-live-nodes.json"
        val outputName = arguments.getString("operatorConfigsFile") ?: "ceho-comparison-configs.json"
        require(snapshotName.matches(Regex("[A-Za-z0-9_.-]+")) && outputName.matches(Regex("[A-Za-z0-9_.-]+")))
        val indexArgument = arguments.getString("operatorSourceIndex") ?: "0"
        val sourceIndex = indexArgument.toIntOrNull()
        require(sourceIndex != null && sourceIndex in 0..500)
        val sources = org.json.JSONObject(java.io.File(context.filesDir, snapshotName).readText()).getJSONArray("inputs")
        val encoded = (0 until sources.length()).map { sources.getJSONObject(it) }
            .single { it.getInt("inputIndex") == sourceIndex }.getString("profile")
        val nodes = ProfileCodec.decode(encoded).nodes
        val selected = DiagnosticNodeIndices.parse(arguments.getString("diagnosticNodeIndices"), nodes.size)
        val configs = org.json.JSONArray()
        for (index in selected) {
            val config = ru.cebers.cehoproxy.vpn.AndroidSystemTrustBundle.applyTo(context, RoutingConfig.build(nodes[index], RoutePolicy()))
            Libbox.checkConfig(config)
            configs.put(org.json.JSONObject().put("nodeIndex", index).put("config", org.json.JSONObject(config)))
        }
        val direct = org.json.JSONObject().put("log", org.json.JSONObject().put("level", "error"))
            .put("dns", org.json.JSONObject().put("servers", org.json.JSONArray().put(org.json.JSONObject().put("type", "local").put("tag", "dns-direct"))).put("final", "dns-direct"))
            .put("outbounds", org.json.JSONArray().put(org.json.JSONObject().put("type", "direct").put("tag", "direct")))
            .put("route", org.json.JSONObject().put("auto_detect_interface", true).put("default_domain_resolver", "dns-direct").put("final", "direct"))
        Libbox.checkConfig(direct.toString())
        configs.put(org.json.JSONObject().put("nodeIndex", -1).put("control", "direct").put("config", direct))
        val current = ru.cebers.cehoproxy.core.ProfileStore(context).read()
        val controlNode = current.selectedNode ?: error("Current profile must contain the Hysteria2 control node")
        require(controlNode.type == "hysteria2") { "Current selected control node must be Hysteria2" }
        val controlConfig = ru.cebers.cehoproxy.vpn.AndroidSystemTrustBundle.applyTo(context, RoutingConfig.build(controlNode, RoutePolicy()))
        Libbox.checkConfig(controlConfig)
        configs.put(org.json.JSONObject().put("nodeIndex", -2).put("control", "hysteria2").put("config", org.json.JSONObject(controlConfig)))
        val output = org.json.JSONObject().put("sourceIndex", sourceIndex).put("filesDir", context.filesDir.absolutePath)
            .put("coreVersion", Libbox.version()).put("remoteFetchPerformed", false).put("diagnosticOnly", true).put("nodes", configs)
        java.io.File(context.filesDir, outputName).writeText(output.toString(2))
        assertEquals(selected.size + 2, configs.length())
    }

    @Test fun privateSnapshotFingerprintHasLiveOutboundEvidence() {
        val arguments = InstrumentationRegistry.getArguments()
        val fingerprint = arguments.getString("uTlsFingerprint")
        assumeTrue("Diagnostic fingerprint must be explicitly supplied", fingerprint != null)
        require(fingerprint in setOf("firefox", "chrome"))
        val dnsBackend = arguments.getString("diagnosticDnsBackend") ?: "production"
        require(dnsBackend in setOf("production", "local", "ipv4-only"))
        val sourceIndexArgument = arguments.getString("operatorSourceIndex") ?: "2"
        val sourceIndex = sourceIndexArgument.toIntOrNull()
        require(sourceIndex != null && sourceIndex in 0..500) { "operatorSourceIndex must be an integer from 0 to 500" }
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val snapshotName = arguments.getString("operatorSnapshotFile") ?: "ceho-live-nodes.json"
        val resultName = arguments.getString("operatorResultFile") ?: "ceho-fingerprint-results.json"
        require(snapshotName.matches(Regex("[A-Za-z0-9_.-]+")))
        require(resultName.matches(Regex("[A-Za-z0-9_.-]+")))
        val sources = org.json.JSONObject(java.io.File(context.filesDir, snapshotName).readText()).getJSONArray("inputs")
        val encoded = (0 until sources.length()).map { sources.getJSONObject(it) }
            .single { it.getInt("inputIndex") == sourceIndex }.getString("profile")
        val nodes = ProfileCodec.decode(encoded).nodes
        val selected = DiagnosticNodeIndices.parse(arguments.getString("diagnosticNodeIndices"), nodes.size)
        val connectivity = context.getSystemService(android.net.ConnectivityManager::class.java)
        val physical = connectivity.allNetworks.firstOrNull { network ->
            connectivity.getNetworkCapabilities(network)?.let {
                it.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
                    it.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_NOT_VPN) &&
                    it.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_VALIDATED)
            } == true
        }
        check(physical != null) { "Validated physical network unavailable" }
        val report = org.json.JSONObject().put("uTlsFingerprint", fingerprint)
            .put("diagnosticDnsBackend", dnsBackend).put("diagnosticOnly", true)
            .put("coreVersion", Libbox.version()).put("coreGoVersion", Libbox.goVersion())
            .put("sourceIndex", sourceIndex).put("snapshotNodes", nodes.size).put("remoteFetchPerformed", false)
            .put("targetHost", "checkip.amazonaws.com")
            .put("nativeSocketsPhysicallyBound", true)
            .put("physicalInterface", connectivity.getLinkProperties(physical)?.interfaceName)
        val results = org.json.JSONArray()
        report.put("nodes", results)
        val resultFile = java.io.File(context.filesDir, resultName)
        val nativeErrorFile = java.io.File(context.filesDir, "ceho-fingerprint-native-errors.log")
        nativeErrorFile.writeText("")
        var failures = 0
        for (nodeIndex in selected) {
            val result = org.json.JSONObject().put("nodeIndex", nodeIndex).put("type", nodes[nodeIndex].type)
            results.put(result)
            val started = System.nanoTime()
            var stage = "config"
            try {
                val original = org.json.JSONObject(RoutingConfig.build(nodes[nodeIndex], RoutePolicy()))
                val utls = original.getJSONArray("outbounds").getJSONObject(0).getJSONObject("tls").getJSONObject("utls")
                check(utls.getBoolean("enabled"))
                utls.put("fingerprint", fingerprint)
                if (dnsBackend == "ipv4-only") original.getJSONObject("dns").put("strategy", "ipv4_only")
                if (dnsBackend == "local") {
                    val servers = original.getJSONObject("dns").getJSONArray("servers")
                    val remoteIndex = (0 until servers.length()).single {
                        servers.getJSONObject(it).getString("tag") == "dns-remote"
                    }
                    servers.put(remoteIndex, org.json.JSONObject().put("type", "local").put("tag", "dns-remote"))
                }
                val generated = ru.cebers.cehoproxy.vpn.AndroidSystemTrustBundle.applyTo(context, original.toString())
                Libbox.checkConfig(generated)
                result.put("configAccepted", true)
                nativeErrorFile.appendText("nodeIndex=$nodeIndex fingerprint=$fingerprint\n")
                result.put("exitIp", probeNativeOutbound(generated, connectivity, physical,
                    "checkip.amazonaws.com", nativeErrorFile) { stage = it })
                result.put("httpsSucceeded", true)
            } catch (error: Throwable) {
                failures++
                result.put("httpsSucceeded", false).put("failureStage", stage).put("errorClass", error.javaClass.simpleName)
            } finally {
                result.put("durationMs", (System.nanoTime() - started) / 1_000_000)
                resultFile.writeText(report.toString(2))
            }
        }
        report.put("accountedNodes", results.length()).put("httpsSucceededNodes", selected.size - failures)
            .put("complete", results.length() == selected.size)
        resultFile.writeText(report.toString(2))
        assertEquals("Diagnostic nodes must all be accounted for", selected.size, results.length())
        assertEquals("Fingerprint diagnostic failed; inspect sanitized private report", 0, failures)
    }

    private fun probeNativeOutbound(config: String, connectivity: android.net.ConnectivityManager,
        physical: android.net.Network, targetHost: String, nativeErrorFile: java.io.File,
        phase: (String) -> Unit): String {
        val port = java.net.ServerSocket(0, 1, java.net.InetAddress.getByName("127.0.0.1")).use { it.localPort }
        val fixture = org.json.JSONObject(config).put("log", org.json.JSONObject()
            .put("level", "error").put("output", nativeErrorFile.absolutePath))
            .put("inbounds", org.json.JSONArray().put(org.json.JSONObject().put("type", "socks")
                .put("listen", "127.0.0.1").put("listen_port", port)))
        val platform = java.lang.reflect.Proxy.newProxyInstance(io.nekohasekai.libbox.PlatformInterface::class.java.classLoader,
            arrayOf(io.nekohasekai.libbox.PlatformInterface::class.java)) { _, method, args ->
            when (method.name) {
                "findConnectionOwner" -> io.nekohasekai.libbox.ConnectionOwner().apply {
                    userId = android.os.Process.myUid()
                    userName = "ru.cebers.cehoproxy"
                    setAndroidPackageNames(object : io.nekohasekai.libbox.StringIterator {
                        private var available = true
                        override fun hasNext() = available
                        override fun len() = 1
                        override fun next(): String { available = false; return "ru.cebers.cehoproxy" }
                    })
                }
                "getInterfaces" -> ru.cebers.cehoproxy.vpn.CehoVpnService().getInterfaces()
                "usePlatformAutoDetectInterfaceControl" -> true
                "autoDetectInterfaceControl" -> {
                    val fd = (args!![0] as Number).toInt()
                    val capabilities = connectivity.getNetworkCapabilities(physical)
                    check(capabilities != null &&
                        capabilities.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
                        capabilities.hasCapability(android.net.NetworkCapabilities.NET_CAPABILITY_NOT_VPN) &&
                        !capabilities.hasTransport(android.net.NetworkCapabilities.TRANSPORT_VPN)) {
                        "Physical network unavailable for isolated native fixture"
                    }
                    // This isolated engine has no VpnService; bind its outbound sockets explicitly.
                    android.os.ParcelFileDescriptor.fromFd(fd).use { duplicate ->
                        physical.bindSocket(duplicate.fileDescriptor)
                    }
                    null
                }
                "localDNSTransport" -> ru.cebers.cehoproxy.vpn.AndroidLocalDnsTransport { physical }
                "startDefaultInterfaceMonitor" -> {
                    val listener = args!![0] as io.nekohasekai.libbox.InterfaceUpdateListener
                    val name = connectivity.getLinkProperties(physical)?.interfaceName ?: error("Physical interface unavailable")
                    val index = java.net.NetworkInterface.getByName(name)?.index ?: error("Physical interface index unavailable")
                    listener.updateDefaultInterface(name, index, false, false)
                    null
                }
                else -> when (method.returnType) {
                    java.lang.Boolean.TYPE -> false
                    java.lang.Integer.TYPE -> -1
                    String::class.java -> ""
                    else -> null
                }
            }
        } as io.nekohasekai.libbox.PlatformInterface
        val handler = java.lang.reflect.Proxy.newProxyInstance(io.nekohasekai.libbox.CommandServerHandler::class.java.classLoader,
            arrayOf(io.nekohasekai.libbox.CommandServerHandler::class.java)) { _, method, args ->
            when (method.name) {
                "getSystemProxyStatus" -> io.nekohasekai.libbox.SystemProxyStatus().apply { available = false; enabled = false }
                "connectSSHAgent" -> -1
                "writeDebugMessage" -> {
                    synchronized(nativeErrorFile) { nativeErrorFile.appendText(args?.firstOrNull()?.toString().orEmpty() + "\n") }
                    null
                }
                else -> null
            }
        } as io.nekohasekai.libbox.CommandServerHandler
        check(!ru.cebers.cehoproxy.vpn.CehoVpnService.hasLiveServiceInstance()) {
            "Stop the real VPN before this isolated native fixture"
        }
        val engine = io.nekohasekai.libbox.CommandServer(handler, platform)
        try {
            phase("fixtureConfig")
            Libbox.checkConfig(fixture.toString())
            phase("engineStart")
            engine.start()
            engine.startOrReloadService(fixture.toString(), io.nekohasekai.libbox.OverrideOptions())
            val socket = java.net.Socket(java.net.Proxy(java.net.Proxy.Type.SOCKS, java.net.InetSocketAddress("127.0.0.1", port)))
            socket.use {
                phase("connect")
                socket.connect(java.net.InetSocketAddress.createUnresolved(targetHost, 443), 12_000)
                ((javax.net.ssl.SSLSocketFactory.getDefault() as javax.net.ssl.SSLSocketFactory).createSocket(socket, targetHost, 443, true) as javax.net.ssl.SSLSocket).use { tls ->
                    tls.soTimeout = 12_000
                    tls.sslParameters = tls.sslParameters.apply {
                        serverNames = listOf(javax.net.ssl.SNIHostName(targetHost))
                        endpointIdentificationAlgorithm = "HTTPS"
                    }
                    phase("tls")
                    tls.startHandshake()
                    tls.outputStream.write("GET / HTTP/1.1\r\nHost: $targetHost\r\nConnection: close\r\n\r\n".toByteArray(Charsets.US_ASCII))
                    tls.outputStream.flush()
                    phase("http")
                    val response = tls.inputStream.bufferedReader().readText()
                    check(response.startsWith("HTTP/1.1 200") || response.startsWith("HTTP/1.0 200"))
                    val body = response.substringAfter("\r\n\r\n").trim()
                    check(body.matches(Regex("[0-9a-fA-F:.]+")) && body.length in 3..45)
                    return body
                }
            }
        } finally {
            runCatching { engine.closeService() }
            engine.close()
        }
    }

    @Test fun suppliedSubscriptionIsFetchedParsedAndAcceptedByEngine() {
        val url = InstrumentationRegistry.getArguments().getString("subscriptionUrl")
        assumeTrue("Live endpoint must be provided by the test operator", !url.isNullOrBlank())
        val response = SubscriptionFetcher.fetchWithMetadata(url!!, SubscriptionRequestOptions())
        val nodes = response.parseNodes()
        assertTrue("Subscription must produce at least one node", nodes.isNotEmpty())
        val rejected = nodes.count { runCatching { Libbox.checkConfig(RoutingConfig.build(it, RoutePolicy())) }.isFailure }
        assertEquals("Nodes rejected by engine", 0, rejected)
        val routePackage = InstrumentationRegistry.getArguments().getString("routePackage")
        if (!routePackage.isNullOrBlank()) {
            val context = InstrumentationRegistry.getInstrumentation().targetContext
            val profile = ClientProfile(
                nodes = nodes,
                apps = listOf(routePackage),
                excludeSelectedApps = InstrumentationRegistry.getArguments().getString("routeMode") == "exclude",
            )
            context.getSharedPreferences("profile", 0).edit().putString("data", ProfileCodec.encode(profile)).commit()
            context.getSharedPreferences("onboarding", 0).edit().putBoolean("completed", true).commit()
        }
    }
}
