package ru.cebers.cehoproxy.core

import java.net.URI
import java.net.URLDecoder
import java.net.URLEncoder

/** Address a receiving device shows as a QR code and announces in the local network, so a phone can hand it subscriptions. */
internal object ShareLink {
    private val path = Regex("^/p/([0-9a-f]{32})$")
    const val ATTRIBUTE_TOKEN = "t"
    const val ATTRIBUTE_HOST = "h"

    fun url(host: String, port: Int, token: String): String = "http://$host:$port/p/$token"

    fun isPairingUrl(text: String): Boolean {
        val uri = runCatching { URI(text.trim()) }.getOrNull() ?: return false
        return uri.scheme == "http" && uri.host != null && uri.port in 1..65535 && path.matches(uri.rawPath.orEmpty()) && uri.rawQuery == null
    }

    fun tokenOf(requestPath: String): String? = path.find(requestPath.substringBefore('?'))?.groupValues?.get(1)

    /** Link the receiver page offers to a phone that already has CehoProxy; the app opens and sends its subscriptions. */
    fun appLink(pairingUrl: String): String = "cehoproxy://pair?u=" + URLEncoder.encode(pairingUrl, "UTF-8")

    fun fromAppLink(link: String): String? {
        val uri = runCatching { URI(link.trim()) }.getOrNull() ?: return null
        if (uri.scheme != "cehoproxy" || uri.host != "pair") return null
        val value = uri.rawQuery.orEmpty().split('&').firstNotNullOfOrNull { pair ->
            if (pair.substringBefore('=') == "u") runCatching { URLDecoder.decode(pair.substringAfter('=', ""), "UTF-8") }.getOrNull() else null
        } ?: return null
        return value.takeIf(::isPairingUrl)
    }

    /** Body of the receiver page form: `link=<url-encoded text>`. */
    fun formField(body: String, name: String): String? = body.split('&').firstNotNullOfOrNull { pair ->
        if (pair.substringBefore('=') == name) runCatching { URLDecoder.decode(pair.substringAfter('=', ""), "UTF-8") }.getOrNull() else null
    }?.trim()?.takeIf { it.isNotEmpty() }
}
