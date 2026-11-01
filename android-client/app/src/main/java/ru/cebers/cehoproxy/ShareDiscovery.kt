package ru.cebers.cehoproxy

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.os.Handler
import android.os.Looper
import ru.cebers.cehoproxy.core.ShareLink
import java.net.Inet4Address

/** Finds devices in the same network that are waiting for subscriptions. */
internal class ShareDiscovery(context: Context, private val onChange: (List<Device>) -> Unit) {
    data class Device(val name: String, val url: String)

    private val nsd = context.getSystemService(NsdManager::class.java)
    private val main = Handler(Looper.getMainLooper())
    private val found = LinkedHashMap<String, Device>()
    private val queue = ArrayDeque<NsdServiceInfo>()
    private var resolving = false
    private var active = false

    private val discovery = object : NsdManager.DiscoveryListener {
        override fun onDiscoveryStarted(serviceType: String) {}
        override fun onDiscoveryStopped(serviceType: String) {}
        override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {}
        override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {}
        override fun onServiceFound(info: NsdServiceInfo) = main.post { queue.addLast(info); resolveNext() }.let {}
        override fun onServiceLost(info: NsdServiceInfo) = main.post { if (found.remove(info.serviceName) != null) onChange(found.values.toList()) }.let {}
    }

    fun start() {
        if (active || nsd == null) return
        active = runCatching { nsd.discoverServices(ShareReceiver.SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, discovery) }.isSuccess
    }

    fun stop() {
        if (active) runCatching { nsd?.stopServiceDiscovery(discovery) }
        active = false
        queue.clear()
    }

    // Older Android resolves one service at a time.
    @Suppress("DEPRECATION")
    private fun resolveNext() {
        if (resolving || !active) return
        val next = queue.removeFirstOrNull() ?: return
        resolving = true
        val listener = object : NsdManager.ResolveListener {
            override fun onResolveFailed(info: NsdServiceInfo, errorCode: Int) = main.post { resolving = false; resolveNext() }.let {}
            override fun onServiceResolved(info: NsdServiceInfo) = main.post {
                resolving = false
                val token = info.attributes[ShareLink.ATTRIBUTE_TOKEN]?.toString(Charsets.UTF_8)
                val host = info.attributes[ShareLink.ATTRIBUTE_HOST]?.toString(Charsets.UTF_8) ?: (info.host as? Inet4Address)?.hostAddress
                if (token != null && host != null && active) {
                    val url = ShareLink.url(host, info.port, token)
                    if (ShareLink.isPairingUrl(url)) { found[info.serviceName] = Device(info.serviceName, url); onChange(found.values.toList()) }
                }
                resolveNext()
            }.let {}
        }
        if (runCatching { nsd?.resolveService(next, listener) }.isFailure) { resolving = false; resolveNext() }
    }
}
