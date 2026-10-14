package ru.cebers.cehoproxy.core

import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.URL
import java.util.Locale

internal data class VerifiedNodeCountry(val countryCode: String, val exitIp: String, val checkedAt: Long = 0)

internal object NodeExitCountryProbe {
    fun parse(body: String): VerifiedNodeCountry? = runCatching {
        val json = JSONObject(body)
        val code = json.optString("country").uppercase(Locale.ROOT)
        if (code !in Locale.getISOCountries().toSet()) return null
        val ip = json.optString("ip")
        if (!isPublicIp(ip)) return null
        VerifiedNodeCountry(code, ip)
    }.getOrNull()

    fun fetch(proxyPort: Int): VerifiedNodeCountry? {
        if (proxyPort !in 1..65535) return null
        return runCatching {
            val proxy = Proxy(Proxy.Type.HTTP, InetSocketAddress("127.0.0.1", proxyPort))
            val connection = URL("https://api.country.is/").openConnection(proxy) as HttpURLConnection
            try {
                connection.connectTimeout = 4000
                connection.readTimeout = 4000
                connection.instanceFollowRedirects = false
                connection.setRequestProperty("Connection", "close")
                if (connection.responseCode != 200) return null
                val body = connection.inputStream.use { input ->
                    val output = java.io.ByteArrayOutputStream()
                    val buffer = ByteArray(512)
                    while (output.size() <= 2048) {
                        val read = input.read(buffer, 0, minOf(buffer.size, 2049 - output.size()))
                        if (read < 0) break
                        output.write(buffer, 0, read)
                    }
                    output.toByteArray()
                }
                if (body.size > 2048) return null
                parse(body.toString(Charsets.UTF_8))?.copy(checkedAt = System.currentTimeMillis())
            } finally { connection.disconnect() }
        }.getOrNull()
    }

    private fun isPublicIp(value: String): Boolean {
        if (value.isBlank() || value.length > 45 || '%' in value) return false
        if (':' in value) {
            if (!value.matches(Regex("[0-9A-Fa-f:]+"))) return false
        } else {
            val parts = value.split('.')
            if (parts.size != 4 || parts.any { it.isEmpty() || it.length > 3 || it.any { c -> !c.isDigit() } || it.toIntOrNull() !in 0..255 }) return false
        }
        val address = runCatching { InetAddress.getByName(value) }.getOrNull() ?: return false
        if (address.isAnyLocalAddress || address.isLoopbackAddress || address.isLinkLocalAddress || address.isSiteLocalAddress || address.isMulticastAddress) return false
        val bytes = address.address
        if (bytes.size == 4) {
            val a = bytes[0].toInt() and 255; val b = bytes[1].toInt() and 255
            if (a == 0 || a >= 240 || (a == 100 && b in 64..127) || (a == 198 && b in 18..19) || (a == 192 && b == 0 && (bytes[2].toInt() and 255) in listOf(0, 2)) || (a == 198 && b == 51 && (bytes[2].toInt() and 255) == 100) || (a == 203 && b == 0 && (bytes[2].toInt() and 255) == 113)) return false
        } else if (bytes.size == 16) {
            if ((bytes[0].toInt() and 0xFE) == 0xFC || (bytes[0].toInt() and 0xE0) != 0x20) return false
            if ((bytes[0].toInt() and 255) == 0x20 && (bytes[1].toInt() and 255) == 0x01 && (bytes[2].toInt() and 255) == 0x0D && (bytes[3].toInt() and 255) == 0xB8) return false
        } else return false
        return true
    }
}
