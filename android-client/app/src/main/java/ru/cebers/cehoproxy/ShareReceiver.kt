package ru.cebers.cehoproxy

import android.content.Context
import android.net.ConnectivityManager
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.NetworkCapabilities
import android.os.Handler
import android.os.Looper
import ru.cebers.cehoproxy.core.ShareLink
import java.io.BufferedInputStream
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URI
import java.security.SecureRandom

/** One-shot web server on the receiving device, announced in the local network: a phone finds it or scans its QR code and posts subscriptions. */
internal class ShareReceiver(private val onPayload: (String) -> Unit) {
    private val main = Handler(Looper.getMainLooper())
    private val token = ByteArray(16).also { SecureRandom().nextBytes(it) }.joinToString("") { "%02x".format(it) }
    @Volatile private var server: ServerSocket? = null
    private var nsd: NsdManager? = null
    private var announcement: NsdManager.RegistrationListener? = null

    /** Returns the address to show as a QR code, or null when the device has no Wi-Fi or Ethernet address. */
    fun start(context: Context, deviceName: String): String? {
        val host = lanAddress(context) ?: return null
        val socket = ServerSocket().apply { reuseAddress = true; bind(InetSocketAddress(0), 4) }
        server = socket
        Thread({ serve(socket) }, "ceho-share-receiver").apply { isDaemon = true }.start()
        announce(context, deviceName, host, socket.localPort)
        main.postDelayed({ stop() }, LIFETIME_MILLIS)
        return ShareLink.url(host, socket.localPort, token)
    }

    fun stop() {
        main.removeCallbacksAndMessages(null)
        announcement?.let { listener -> runCatching { nsd?.unregisterService(listener) } }
        announcement = null
        runCatching { server?.close() }
        server = null
    }

    private fun announce(context: Context, deviceName: String, host: String, port: Int) {
        val manager = context.getSystemService(NsdManager::class.java) ?: return
        val info = NsdServiceInfo().apply {
            serviceName = deviceName
            serviceType = SERVICE_TYPE
            this.port = port
            setAttribute(ShareLink.ATTRIBUTE_TOKEN, token)
            setAttribute(ShareLink.ATTRIBUTE_HOST, host)
        }
        val listener = object : NsdManager.RegistrationListener {
            override fun onServiceRegistered(info: NsdServiceInfo) {}
            override fun onRegistrationFailed(info: NsdServiceInfo, error: Int) {}
            override fun onServiceUnregistered(info: NsdServiceInfo) {}
            override fun onUnregistrationFailed(info: NsdServiceInfo, error: Int) {}
        }
        if (runCatching { manager.registerService(info, NsdManager.PROTOCOL_DNS_SD, listener) }.isSuccess) {
            nsd = manager
            announcement = listener
        }
    }

    private fun serve(socket: ServerSocket) {
        while (!socket.isClosed) {
            val client = runCatching { socket.accept() }.getOrNull() ?: break
            runCatching { client.use { handle(it) } }
        }
    }

    private fun handle(client: Socket) {
        client.soTimeout = 10_000
        val input = BufferedInputStream(client.getInputStream())
        val requestLine = readLine(input) ?: return
        val headers = generateSequence { readLine(input)?.takeIf { it.isNotEmpty() } }.associate { it.substringBefore(':').trim().lowercase() to it.substringAfter(':', "").trim() }
        val method = requestLine.substringBefore(' ')
        val path = requestLine.substringAfter(' ').substringBefore(' ')
        if (ShareLink.tokenOf(path) != token) return respond(client, 404, page("Ссылка устарела", "На устройстве снова откройте «Получить с телефона»."))
        when (method) {
            "GET" -> respond(client, 200, formPage(headers["host"]))
            "POST" -> {
                val length = headers["content-length"]?.toIntOrNull() ?: return respond(client, 411, page("Ошибка", "Устройство не поняло запрос."))
                if (length !in 1..MAX_BODY) return respond(client, 413, page("Слишком много данных", "Отправьте только ссылку на подписку."))
                val body = String(readBytes(input, length), Charsets.UTF_8)
                val payload = if (headers["content-type"].orEmpty().startsWith("application/x-www-form-urlencoded")) ShareLink.formField(body, "link") else body.trim().takeIf { it.isNotEmpty() }
                if (payload == null) return respond(client, 400, formPage(headers["host"], "Вставьте ссылку на подписку."))
                respond(client, 200, page("Отправлено", "Подтвердите добавление на экране устройства."))
                stop()
                main.post { onPayload(payload) }
            }
            else -> respond(client, 405, page("Ошибка", "Устройство не поняло запрос."))
        }
    }

    private fun formPage(host: String?, error: String? = null): String {
        val self = "/p/$token"
        val appLink = host?.takeIf { HOST.matches(it) }?.let { ShareLink.appLink("http://$it$self") }
        return page("Отправить подписку на устройство", listOfNotNull(
            appLink?.let { "<p>Если CehoProxy установлен на этом телефоне, отправьте подписки из приложения:</p><a class=\"button\" href=\"$it\">Отправить из CehoProxy</a>" },
            "<p>${if (appLink != null) "Или вставьте" else "Вставьте"} ссылку на подписку:</p>",
            error?.let { "<p class=\"error\">$it</p>" },
            "<form method=\"post\" action=\"$self\"><textarea name=\"link\" rows=\"4\" placeholder=\"https://…\"></textarea><button type=\"submit\">Отправить на устройство</button></form>",
        ).joinToString("\n"))
    }

    private fun page(title: String, body: String) = """<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="dark"><title>$title</title>
        <style>body{font-family:system-ui,sans-serif;margin:0;padding:24px;background:#1c1b17;color:#e9e6dc;line-height:1.5}h1{font-size:24px;margin:0 0 16px}p{color:#c7c4b8}
        .button,button{display:block;width:100%;box-sizing:border-box;margin:12px 0 24px;padding:14px;border:0;border-radius:14px;background:#a9c29a;color:#1f2a1b;font-size:16px;font-weight:600;text-align:center;text-decoration:none}
        textarea{width:100%;box-sizing:border-box;padding:12px;border-radius:12px;border:1px solid #4a4a40;background:#26251f;color:#e9e6dc;font-size:15px}.error{color:#f2b8b5}</style></head>
        <body><h1>$title</h1>$body</body></html>"""

    private fun respond(client: Socket, status: Int, html: String) {
        val body = html.toByteArray(Charsets.UTF_8)
        val reason = when (status) { 200 -> "OK"; 400 -> "Bad Request"; 404 -> "Not Found"; 405 -> "Method Not Allowed"; 411 -> "Length Required"; else -> "Payload Too Large" }
        client.getOutputStream().apply {
            write("HTTP/1.1 $status $reason\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: ${body.size}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n".toByteArray(Charsets.US_ASCII))
            write(body)
            flush()
        }
    }

    private fun readLine(input: InputStream): String? {
        val line = ByteArrayOutputStream()
        while (true) {
            val b = input.read()
            if (b == -1) return if (line.size() == 0) null else line.toString(Charsets.US_ASCII.name())
            if (b == '\n'.code) return line.toString(Charsets.US_ASCII.name()).trimEnd('\r')
            if (line.size() > 8_192) return null
            line.write(b)
        }
    }

    private fun readBytes(input: InputStream, length: Int): ByteArray {
        val out = ByteArray(length)
        var read = 0
        while (read < length) {
            val n = input.read(out, read, length - read)
            if (n < 0) break
            read += n
        }
        return out.copyOf(read)
    }

    private fun lanAddress(context: Context): String? {
        val cm = context.getSystemService(ConnectivityManager::class.java) ?: return null
        @Suppress("DEPRECATION")
        return cm.allNetworks.asSequence().filter { network ->
            val caps = cm.getNetworkCapabilities(network) ?: return@filter false
            !caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN) && (caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET))
        }.flatMap { cm.getLinkProperties(it)?.linkAddresses.orEmpty().asSequence() }
            .map { it.address }.firstOrNull { it is Inet4Address && !it.isLoopbackAddress }?.hostAddress
    }

    companion object {
        const val SERVICE_TYPE = "_cehoproxy._tcp"
        private const val MAX_BODY = 1_000_000
        private const val LIFETIME_MILLIS = 10 * 60_000L
        private val HOST = Regex("^[0-9A-Za-z.:\\-]{1,64}$")

        /** Sends subscriptions from the phone app. Plain sockets: the address is in the home network and has no certificate. */
        fun send(url: String, payload: String): Boolean = runCatching {
            val uri = URI(url)
            Socket().use { socket ->
                socket.connect(InetSocketAddress(uri.host, uri.port), 5_000)
                socket.soTimeout = 15_000
                val body = payload.toByteArray(Charsets.UTF_8)
                socket.getOutputStream().apply {
                    write("POST ${uri.rawPath} HTTP/1.1\r\nHost: ${uri.host}:${uri.port}\r\nContent-Type: application/json\r\nContent-Length: ${body.size}\r\nConnection: close\r\n\r\n".toByteArray(Charsets.US_ASCII))
                    write(body)
                    flush()
                }
                socket.getInputStream().bufferedReader(Charsets.US_ASCII).readLine()?.split(' ')?.getOrNull(1) == "200"
            }
        }.getOrDefault(false)
    }
}
