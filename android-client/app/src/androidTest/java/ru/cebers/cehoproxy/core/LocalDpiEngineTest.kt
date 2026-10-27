package ru.cebers.cehoproxy.core

import androidx.test.platform.app.InstrumentationRegistry
import io.nekohasekai.libbox.*
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import ru.cebers.cehoproxy.vpn.CehoVpnService
import java.io.ByteArrayOutputStream
import java.lang.reflect.Proxy as JavaProxy
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.ServerSocket
import java.net.Socket
import java.security.KeyStore
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import javax.net.ssl.*

class LocalDpiEngineTest {
    @Test fun nativeTlsRecordFragmentationPreservesHttpsOnControlledLocalServer() {
        val testContext = InstrumentationRegistry.getInstrumentation().context
        val password = "test-fixture-only".toCharArray()
        val keys = KeyStore.getInstance("PKCS12").apply {
            testContext.assets.open("dpi-fixture.p12").use { load(it, password) }
        }
        val keyManager = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm()).apply { init(keys, password) }
        val trustManager = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(keys) }
        val tls = SSLContext.getInstance("TLS").apply { init(keyManager.keyManagers, trustManager.trustManagers, null) }
        val server = tls.serverSocketFactory.createServerSocket(0, 1, java.net.InetAddress.getByName("127.0.0.1")) as SSLServerSocket
        server.soTimeout = 10_000
        val recorder = ServerSocket(0, 1, java.net.InetAddress.getByName("127.0.0.1")).apply { soTimeout = 10_000 }
        val nativePort = ServerSocket(0).use { it.localPort }
        val executor = Executors.newFixedThreadPool(3)
        val captured = ByteArrayOutputStream()
        val platform = JavaProxy.newProxyInstance(PlatformInterface::class.java.classLoader, arrayOf(PlatformInterface::class.java)) { _, method, _ ->
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
                "writeDebugMessage" -> { android.util.Log.d("CehoDpiFixture", "native test callback"); null }
                else -> null
            }
        } as CommandServerHandler
        val engine = CommandServer(handler, platform)
        try {
            val https = executor.submit<String> {
                (server.accept() as SSLSocket).use { connection ->
                    connection.soTimeout = 10_000
                    val request = connection.inputStream.bufferedReader().readLine()
                    val response = "HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\ndpi-ok"
                    connection.outputStream.write(response.toByteArray(Charsets.US_ASCII))
                    connection.outputStream.flush()
                    request
                }
            }
            val relay = executor.submit<Unit> {
                recorder.accept().use { incoming ->
                    incoming.soTimeout = 10_000
                    Socket("127.0.0.1", server.localPort).use { outgoing ->
                        outgoing.soTimeout = 10_000
                        val responses = executor.submit<Unit> { outgoing.inputStream.copyTo(incoming.outputStream) }
                        val buffer = ByteArray(8192)
                        while (true) {
                            val count = incoming.inputStream.read(buffer)
                            if (count < 0) break
                            captured.write(buffer, 0, count)
                            outgoing.outputStream.write(buffer, 0, count)
                            outgoing.outputStream.flush()
                        }
                        responses.get(10, TimeUnit.SECONDS)
                    }
                }
            }
            val generated = JSONObject(RoutingConfig.build(emptyList(), 0, RoutePolicy(
                apps = listOf("com.google.android.youtube"), localDpiApps = listOf("com.google.android.youtube"))))
            val generatedRules = generated.getJSONObject("route").getJSONArray("rules")
            val fragmentRule = (0 until generatedRules.length()).map { generatedRules.getJSONObject(it) }.first { it.optBoolean("tls_fragment") || it.toString().contains("tls_record_fragment") }
            fragmentRule.remove("package_name")
            val config = JSONObject().put("inbounds", JSONArray().put(JSONObject().put("type", "socks")
                .put("listen", "127.0.0.1").put("listen_port", nativePort)))
                .put("outbounds", JSONArray().put(JSONObject().put("type", "direct").put("tag", "direct")))
                .put("route", JSONObject().put("rules", JSONArray().put(fragmentRule)).put("final", "direct")).toString()
            Libbox.checkConfig(config)
            engine.start()
            engine.startOrReloadService(config, OverrideOptions())
            val socket = Socket(Proxy(Proxy.Type.SOCKS, InetSocketAddress("127.0.0.1", nativePort)))
            socket.connect(InetSocketAddress("127.0.0.1", recorder.localPort), 10_000)
            (tls.socketFactory.createSocket(socket, "dpi-fixture.example", recorder.localPort, true) as SSLSocket).use { client ->
                client.soTimeout = 10_000
                client.sslParameters = client.sslParameters.apply {
                    serverNames = listOf(SNIHostName("dpi-fixture.example"))
                    endpointIdentificationAlgorithm = "HTTPS"
                }
                client.startHandshake()
                client.outputStream.write("GET /dpi HTTP/1.1\r\nHost: dpi-fixture.example\r\nConnection: close\r\n\r\n".toByteArray(Charsets.US_ASCII))
                client.outputStream.flush()
                assertTrue(client.inputStream.bufferedReader().readText().endsWith("dpi-ok"))
            }
            assertEquals("GET /dpi HTTP/1.1", https.get(10, TimeUnit.SECONDS))
            relay.get(10, TimeUnit.SECONDS)
            assertTrue("ClientHello must arrive in multiple TLS records", clientHelloRecordCount(captured.toByteArray()) > 1)
        } finally {
            runCatching { engine.closeService() }
            engine.close()
            recorder.close()
            server.close()
            executor.shutdownNow()
        }
    }

    private fun clientHelloRecordCount(data: ByteArray): Int {
        var offset = 0
        var payload = 0
        var required = -1
        var records = 0
        while (offset + 5 <= data.size) {
            if (data[offset].toInt() != 22) break
            val length = ((data[offset + 3].toInt() and 255) shl 8) or (data[offset + 4].toInt() and 255)
            if (offset + 5 + length > data.size) break
            if (required < 0 && length >= 4) {
                assertEquals(1, data[offset + 5].toInt())
                required = 4 + ((data[offset + 6].toInt() and 255) shl 16) + ((data[offset + 7].toInt() and 255) shl 8) + (data[offset + 8].toInt() and 255)
            }
            records++
            payload += length
            if (required >= 0 && payload >= required) return records
            offset += 5 + length
        }
        return 0
    }
}
