package ru.cebers.cehoproxy.core

import java.io.ByteArrayOutputStream
import java.net.Proxy
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import java.util.Base64
import java.util.zip.GZIPInputStream
import java.util.zip.InflaterInputStream

data class SubscriptionRequestOptions(val userAgent: String = DEFAULT_USER_AGENT, val headers: Map<String, String> = emptyMap(), val proxy: Proxy? = null, val connectTimeoutMillis: Int = 12_000, val readTimeoutMillis: Int = 20_000) {
    companion object {
        const val DEFAULT_USER_AGENT = "CehoProxyAndroid/0.1 sing-box/1.14.0"
    }
}

class SubscriptionHttpException(val statusCode: Int) : Exception("HTTP $statusCode")

data class FetchedSubscription(val body: String, val metadata: SubscriptionMetadata) {
    fun parseNodes(): List<ProxyNode> = metadata.attachTo(NodeShareParser.parse(body))
}

object SubscriptionFetcher {
    private const val MAX_BYTES = 8 * 1024 * 1024
    private const val MAX_REDIRECTS = 5
    fun fetch(address: String, options: SubscriptionRequestOptions): String = fetchWithMetadata(address, options).body

    fun fetchWithMetadata(address: String, options: SubscriptionRequestOptions): FetchedSubscription {
        var current = URL(address)
        require(current.protocol == "http" || current.protocol == "https") { "Поддерживаются только HTTP и HTTPS ссылки" }
        var forwardedHeaders = options.headers.toMutableMap()
        var cookies = mutableMapOf<String, String>()
        var redirects = 0
        val initialOrigin = origin(current)
        val auth = current.userInfo?.let { userInfo ->
            val decoded = URLDecoder.decode(userInfo.replace("+", "%2B"), StandardCharsets.UTF_8.name())
            "Basic " + Base64.getEncoder().encodeToString(decoded.toByteArray(StandardCharsets.UTF_8))
        }
        while (true) {
            val connection = current.openConnection(options.proxy ?: Proxy.NO_PROXY) as HttpURLConnection
            connection.instanceFollowRedirects = false
            connection.connectTimeout = options.connectTimeoutMillis
            connection.readTimeout = options.readTimeoutMillis
            connection.requestMethod = "GET"
            connection.setRequestProperty("User-Agent", options.userAgent)
            connection.setRequestProperty("Accept", "*/*")
            connection.setRequestProperty("Accept-Encoding", "gzip, deflate")
            forwardedHeaders.forEach(connection::setRequestProperty)
            if (cookies.isNotEmpty()) connection.setRequestProperty("Cookie", cookies.entries.joinToString("; ") { "${it.key}=${it.value}" })
            if (origin(current) == initialOrigin && auth != null && forwardedHeaders.keys.none { it.equals("Authorization", true) }) {
                connection.setRequestProperty("Authorization", auth)
            }
            val status = connection.responseCode
            connection.headerFields.entries.filter { it.key.equals("Set-Cookie", true) }.forEach { entry ->
                entry.value.forEach { header ->
                    header.substringBefore(';').split('=', limit = 2).takeIf { it.size == 2 && it[0].isNotBlank() }
                        ?.let { cookies[it[0].trim()] = it[1].trim() }
                }
            }
            if (status in 300..399) {
                val location = connection.getHeaderField("Location") ?: throw IllegalStateException("Сервер вернул перенаправление без адреса")
                val next = URL(current, location)
                require(next.protocol == "http" || next.protocol == "https") { "Перенаправление ведёт на неподдерживаемый протокол" }
                if (origin(next) != origin(current)) { forwardedHeaders = mutableMapOf(); cookies = mutableMapOf() }
                connection.disconnect()
                if (redirects++ >= MAX_REDIRECTS) throw IllegalStateException("Слишком много перенаправлений подписки")
                current = next
                continue
            }
            if (status !in 200..299) { connection.disconnect(); throw SubscriptionHttpException(status) }
            val stream = connection.inputStream
            val decoded = when (connection.contentEncoding?.lowercase()?.trim()) {
                "gzip" -> GZIPInputStream(stream)
                "deflate" -> InflaterInputStream(stream)
                null, "", "identity" -> stream
                else -> { stream.close(); connection.disconnect(); throw IllegalStateException("Сжатие ${connection.contentEncoding} не поддерживается") }
            }
            return try {
                val output = ByteArrayOutputStream()
                decoded.use { input ->
                    val buffer = ByteArray(8192)
                    while (true) {
                        val count = input.read(buffer)
                        if (count < 0) break
                        require(output.size() + count <= MAX_BYTES) { "Подписка превышает допустимый размер 8 МиБ" }
                        output.write(buffer, 0, count)
                    }
                }
                val body = String(output.toByteArray(), StandardCharsets.UTF_8)
                FetchedSubscription(body, SubscriptionMetadata.from(connection, body).copy(sourceHost = URL(address).host, sourceUrl = address))
            } finally { connection.disconnect() }
        }
    }

    private fun origin(url: URL): String = "${url.protocol.lowercase()}://${url.host.lowercase()}:${if (url.port == -1) url.defaultPort else url.port}"
}
