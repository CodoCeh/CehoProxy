package ru.cebers.cehoproxy.vpn

import io.nekohasekai.libbox.*
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import ru.cebers.cehoproxy.core.OrderedFailoverConfig
import ru.cebers.cehoproxy.core.ProxyNode
import ru.cebers.cehoproxy.core.RoutePolicy
import ru.cebers.cehoproxy.core.RoutingConfig
import java.lang.reflect.Proxy as JavaProxy
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.Executors

/** Loopback-only native fixture; run with the real VPN stopped. */
class OrderedFailoverEngineTest {
    @Test fun deadFirstUsesSecondAndAllDeadBlocksWithoutRestartOrAssignedRouteChange() {
        assertFalse("Stop the real VPN before this isolated engine fixture", CehoVpnService.hasLiveServiceInstance())
        val executor = Executors.newCachedThreadPool()
        val upstream = ServerSocket(0, 8, InetAddress.getByName("127.0.0.1"))
        val reservations = List(4) { ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")) }
        val ports = reservations.map { it.localPort }
        assertEquals(4, ports.distinct().size)
        val (deadPort, appPort, assignedPort, probePort) = ports
        executor.execute {
            runCatching {
                while (!upstream.isClosed) {
                    val connection = upstream.accept()
                    executor.execute {
                        runCatching {
                            connection.use { socket ->
                                socket.soTimeout = 3000
                                val reader = socket.inputStream.bufferedReader()
                                val connect = reader.readLine()
                                check(connect.startsWith("CONNECT "))
                                while (!reader.readLine().isNullOrEmpty()) { }
                                socket.outputStream.write("HTTP/1.1 200 Connection Established\r\n\r\n".toByteArray())
                                socket.outputStream.flush()
                                check(reader.readLine().startsWith("GET "))
                                while (!reader.readLine().isNullOrEmpty()) { }
                                socket.outputStream.write("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".toByteArray())
                                socket.outputStream.flush()
                            }
                        }
                    }
                }
            }
        }
        val platform = JavaProxy.newProxyInstance(PlatformInterface::class.java.classLoader, arrayOf(PlatformInterface::class.java)) { _, method, args ->
            when (method.name) {
                "getInterfaces" -> CehoVpnService().getInterfaces()
                "findConnectionOwner" -> ConnectionOwner().apply {
                    userId = android.os.Process.myUid()
                    userName = "ru.cebers.cehoproxy"
                    setAndroidPackageNames(object : StringIterator {
                        private var available = true
                        override fun hasNext() = available
                        override fun len() = 1
                        override fun next(): String { available = false; return "ru.cebers.cehoproxy" }
                    })
                }
                "startDefaultInterfaceMonitor" -> {
                    val listener = args!![0] as InterfaceUpdateListener
                    val loopback = java.net.NetworkInterface.getByInetAddress(InetAddress.getByName("127.0.0.1"))
                        ?: error("Loopback interface unavailable for native fixture")
                    listener.updateDefaultInterface(loopback.name, loopback.index, false, false)
                    null
                }
                else -> when (method.returnType) {
                    java.lang.Boolean.TYPE -> false
                    java.lang.Integer.TYPE -> -1
                    String::class.java -> ""
                    else -> null
                }
            }
        } as PlatformInterface
        val handler = JavaProxy.newProxyInstance(CommandServerHandler::class.java.classLoader, arrayOf(CommandServerHandler::class.java)) { _, method, _ ->
            when (method.name) {
                "getSystemProxyStatus" -> SystemProxyStatus().apply { available = false; enabled = false }
                "connectSSHAgent" -> -1
                else -> null
            }
        } as CommandServerHandler
        val engine = CommandServer(handler, platform)
        try {
            val nodes = listOf(deadPort, upstream.localPort).mapIndexed { index, port ->
                ProxyNode("Fixture $index", "http", "127.0.0.1", port, "http://127.0.0.1:$port")
            }
            val config = JSONObject(OrderedFailoverConfig.withProbe(RoutingConfig.build(nodes, 0, RoutePolicy(autoRotationEnabled = true)), probePort))
            val probeInbound = config.getJSONArray("inbounds").getJSONObject(1)
            config.put("inbounds", JSONArray().put(JSONObject().put("type", "mixed").put("tag", "fixture-app").put("listen", "127.0.0.1").put("listen_port", appPort))
                .put(probeInbound).put(JSONObject().put("type", "mixed").put("tag", "fixture-assigned").put("listen", "127.0.0.1").put("listen_port", assignedPort)))
            config.getJSONObject("route").getJSONArray("rules").put(JSONObject().put("inbound", JSONArray().put("fixture-assigned")).put("outbound", "proxy-0"))
            config.getJSONObject("route").put("auto_detect_interface", false)
            Libbox.checkConfig(config.toString())
            reservations.forEach { it.close() }
            engine.start()
            engine.startOrReloadService(config.toString(), OverrideOptions())
            val client = Libbox.newStandaloneCommandClient()
            client.selectOutbound(OrderedFailoverConfig.GLOBAL_TAG, "proxy-0")
            assertFalse(healthy(appPort))
            val probes = mutableListOf<String>()
            fun choose(): String? = OrderedFailover.firstHealthy(listOf("proxy-0", "proxy-1"), { true }) { tag ->
                probes.add(tag)
                client.selectOutbound(OrderedFailoverConfig.PROBE_TAG, tag)
                healthy(probePort)
            }
            assertEquals("proxy-1", choose())
            assertEquals(listOf("proxy-0", "proxy-1"), probes)
            client.selectOutbound(OrderedFailoverConfig.GLOBAL_TAG, "proxy-1")
            assertTrue(healthy(appPort))
            assertFalse("Assigned dead node must stay assigned", healthy(assignedPort))
            upstream.close()
            assertNull(choose())
            client.selectOutbound(OrderedFailoverConfig.GLOBAL_TAG, "block")
            assertFalse("All dead must not fall back to direct", healthy(appPort))
        } finally {
            runCatching { engine.closeService() }
            engine.close()
            upstream.close()
            reservations.forEach { it.close() }
            executor.shutdownNow()
        }
    }

    private fun healthy(port: Int): Boolean = runCatching {
        Socket().use { socket ->
            socket.connect(java.net.InetSocketAddress("127.0.0.1", port), 2000)
            socket.soTimeout = 3000
            socket.outputStream.write("GET http://127.0.0.1:54321/health HTTP/1.1\r\nHost: 127.0.0.1:54321\r\nConnection: close\r\n\r\n".toByteArray())
            socket.outputStream.flush()
            socket.inputStream.bufferedReader().readLine()?.contains(" 204 ") == true
        }
    }.getOrDefault(false)
}
