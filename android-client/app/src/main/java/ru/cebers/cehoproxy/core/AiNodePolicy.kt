package ru.cebers.cehoproxy.core

import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.URL

/** Whether an exit server can be used for Gemini: Google must open through it and its country must be one Google serves. */
internal object AiNodePolicy {
    private val unsupportedCountries = setOf("RU", "BY", "CN", "HK", "MO", "IR", "KP", "SY", "CU", "AF", "MM", "TM")

    fun countrySupported(code: String?): Boolean = !code.isNullOrBlank() && code.uppercase() !in unsupportedCountries

    fun allows(exitCountry: String?, googleReachable: Boolean): Boolean = googleReachable && countrySupported(exitCountry)

    fun googleReachable(proxyPort: Int, timeoutMillis: Int = 6000): Boolean {
        if (proxyPort !in 1..65535) return false
        return runCatching {
            val connection = URL("https://gemini.google.com/app").openConnection(Proxy(Proxy.Type.HTTP, InetSocketAddress("127.0.0.1", proxyPort))) as HttpURLConnection
            try {
                connection.connectTimeout = timeoutMillis
                connection.readTimeout = timeoutMillis
                connection.instanceFollowRedirects = true
                connection.useCaches = false
                connection.setRequestProperty("Connection", "close")
                connection.setRequestProperty("User-Agent", "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/124 Mobile Safari/537.36")
                connection.responseCode == 200 && connection.inputStream.use { it.read(ByteArray(2048)) } > 0
            } finally { connection.disconnect() }
        }.getOrDefault(false)
    }
}
