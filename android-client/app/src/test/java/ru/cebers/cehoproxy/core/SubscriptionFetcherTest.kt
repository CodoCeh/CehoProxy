package ru.cebers.cehoproxy.core

import java.io.ByteArrayOutputStream
import java.net.ServerSocket
import java.net.Socket
import java.nio.charset.StandardCharsets
import java.util.concurrent.atomic.AtomicReference
import java.util.zip.GZIPOutputStream
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SubscriptionFetcherTest {
    @Test fun defaultUserAgentRequestsNativeSingBoxWithoutOverwritingCustomAgent() {
        assertEquals("CehoProxyAndroid/0.1 sing-box/1.14.0", SubscriptionRequestOptions().userAgent)
        val api = LocalHttpServer { request ->
            val native = request.header("User-Agent")?.contains("sing-box/1.14.0") == true
            Response(if (native) 200 else 400, emptyMap(),
                """{"outbounds":[{"type":"hysteria2","tag":"native","server":"edge.example","server_port":8443,"password":"synthetic","tls":{"enabled":true,"insecure":true}}]}""".toByteArray())
        }
        try {
            val fetched = SubscriptionFetcher.fetch("http://127.0.0.1:${api.port}/", SubscriptionRequestOptions())
            val node = NodeShareParser.parse(fetched).single()
            assertTrue(org.json.JSONObject(node.raw).getJSONObject("tls").getBoolean("insecure"))
        } finally { api.close() }
    }

    @Test fun fetchKeepsCookieSetByRedirectForTheSameOrigin() {
        val api = LocalHttpServer { request ->
            if (request.header("Cookie") == "guard=passed") Response(200, emptyMap(), "vless://synthetic@edge.example:443#Node".toByteArray())
            else Response(307, mapOf("Location" to "/subscription", "Set-Cookie" to "guard=passed; Path=/; HttpOnly"), byteArrayOf())
        }
        try {
            assertEquals("vless://synthetic@edge.example:443#Node", SubscriptionFetcher.fetch("http://127.0.0.1:${api.port}/start", SubscriptionRequestOptions()))
        } finally { api.close() }
    }

    @Test fun fetchFollowsRedirectStripsCustomHeadersAndDecodesGzip() {
        val forwarded = AtomicReference("")
        val destination = LocalHttpServer { request ->
            forwarded.set("${request.header("X-Subscription-Key")}|${request.header("Authorization")}|${request.header("User-Agent")}")
            val compressed = ByteArrayOutputStream().also { bytes -> GZIPOutputStream(bytes).use { it.write("vless://synthetic@edge.example:443".toByteArray()) } }.toByteArray()
            Response(200, mapOf("Content-Encoding" to "gzip", "subscription-userinfo" to "upload=12; download=34; total=100; expire=1800000000", "support-url" to "https://t.me/example_support", "profile-title" to "Provider plan"), compressed)
        }
        val origin = LocalHttpServer { _ -> Response(302, mapOf("Location" to "http://127.0.0.1:${destination.port}/sub"), byteArrayOf()) }
        try {
            val value = SubscriptionFetcher.fetchWithMetadata("http://127.0.0.1:${origin.port}/start", SubscriptionRequestOptions("CehoTest/2", mapOf("X-Subscription-Key" to "synthetic-secret")))
            assertEquals("vless://synthetic@edge.example:443", value.body)
            assertEquals(1_800_000_000L, value.metadata.expiresAtEpochSeconds)
            assertEquals(46L, value.metadata.usedBytes)
            assertEquals(100L, value.metadata.totalBytes)
            assertEquals("https://t.me/example_support", value.metadata.supportUrl)
            assertEquals("Provider plan", value.metadata.title)
            assertEquals("null|null|CehoTest/2", forwarded.get())
        } finally { origin.close(); destination.close() }
    }

    @Test fun providerTitleSurvivesFetchParsingProfileRoundTripAndPresentation() {
        val body = "#profile-title: Body fallback\nvless://synthetic@edge.example:443#Node"
        val encodedBody = java.util.Base64.getEncoder().encodeToString(body.toByteArray())
        val api = LocalHttpServer { _ -> Response(200, mapOf("profile-title" to "CebCeh"), encodedBody.toByteArray()) }
        try {
            val response = SubscriptionFetcher.fetchWithMetadata("http://127.0.0.1:${api.port}/subscription", SubscriptionRequestOptions())
            val nodes = response.parseNodes()
            assertEquals("CebCeh", nodes.single().subscriptionMetadata.title)
            val restored = ProfileCodec.decode(ProfileCodec.encode(ClientProfile(nodes = nodes)))
            assertEquals(nodes.single().raw, restored.selectedNode?.raw)
            assertEquals("Подписка: CebCeh", NodePresentation.subscriptionSource(restored.selectedNode!!))
        } finally { api.close() }
    }

    @Test fun fetchSendsConfiguredHeadersAndReturnsHttpErrors() {
        val seen = AtomicReference("")
        val api = LocalHttpServer { request ->
            seen.set("${request.header("Authorization")}|${request.header("User-Agent")}")
            Response(200, emptyMap(), "ok".toByteArray())
        }
        val denied = LocalHttpServer { _ -> Response(401, emptyMap(), byteArrayOf()) }
        try {
            assertEquals("ok", SubscriptionFetcher.fetch("http://127.0.0.1:${api.port}/", SubscriptionRequestOptions("TestAgent", mapOf("Authorization" to "Bearer synthetic"))))
            assertEquals("Bearer synthetic|TestAgent", seen.get())
            val error = runCatching { SubscriptionFetcher.fetch("http://127.0.0.1:${denied.port}/", SubscriptionRequestOptions()) }.exceptionOrNull()
            assertEquals(401, (error as SubscriptionHttpException).statusCode)
        } finally { api.close(); denied.close() }
    }

    private data class Response(val status: Int, val headers: Map<String, String>, val body: ByteArray)

    private class Request(private val headers: Map<String, String>) {
        fun header(name: String): String? = headers.entries.firstOrNull { it.key.equals(name, true) }?.value
    }

    private class LocalHttpServer(private val respond: (Request) -> Response) : AutoCloseable {
        private val server = ServerSocket(0, 1, java.net.InetAddress.getByName("127.0.0.1"))
        val port: Int get() = server.localPort
        private val worker = Thread { runCatching { while (!server.isClosed) server.accept().use(::handle) } }.apply { isDaemon = true; start() }

        private fun handle(socket: Socket) {
            socket.soTimeout = 5_000
            val input = socket.getInputStream().bufferedReader(StandardCharsets.ISO_8859_1)
            input.readLine() ?: return
            val headers = linkedMapOf<String, String>()
            while (true) {
                val line = input.readLine() ?: break
                if (line.isEmpty()) break
                val split = line.indexOf(':')
                if (split > 0) headers[line.substring(0, split).trim()] = line.substring(split + 1).trim()
            }
            val result = respond(Request(headers))
            val statusText = when (result.status) { 200 -> "OK"; 302 -> "Found"; 401 -> "Unauthorized"; else -> "Response" }
            val headerText = buildString {
                append("HTTP/1.1 ${result.status} $statusText\r\n")
                result.headers.forEach { (name, value) -> append("$name: $value\r\n") }
                append("Content-Length: ${result.body.size}\r\nConnection: close\r\n\r\n")
            }.toByteArray(StandardCharsets.ISO_8859_1)
            socket.getOutputStream().apply { write(headerText); write(result.body); flush() }
        }

        override fun close() { server.close(); worker.join(2_000) }
    }
}
