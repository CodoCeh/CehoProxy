package ru.cebers.cehoproxy

import android.content.Context
import ru.cebers.cehoproxy.core.EventLog
import ru.cebers.cehoproxy.core.FetchedSubscription
import ru.cebers.cehoproxy.core.SubscriptionFetcher
import ru.cebers.cehoproxy.core.SubscriptionRequestOptions
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Proxy

object SubscriptionTunnelFetch {
    fun fetch(context: Context, address: String, options: SubscriptionRequestOptions = SubscriptionRequestOptions()): FetchedSubscription {
        val proxy = tunnelProxy(context)
        val started = System.currentTimeMillis()
        fun took() = System.currentTimeMillis() - started
        if (proxy == null) return SubscriptionFetcher.fetchWithMetadata(address, options).also { EventLog.append(context, "подписка загружена напрямую за ${took()} мс (туннеля нет)") }
        // With the tunnel up a provider that is blocked for direct access is common, so the tunnel goes first and the direct way is the fallback.
        return try {
            SubscriptionFetcher.fetchWithMetadata(address, options.copy(proxy = proxy, connectTimeoutMillis = 8_000, readTimeoutMillis = 12_000)).also { EventLog.append(context, "подписка загружена через туннель за ${took()} мс") }
        } catch (error: IOException) {
            EventLog.append(context, "подписка через туннель не открылась за ${took()} мс: ${error.javaClass.simpleName}, пробуем напрямую")
            SubscriptionFetcher.fetchWithMetadata(address, options).also { EventLog.append(context, "подписка загружена напрямую за ${took()} мс") }
        }
    }

    private fun tunnelProxy(context: Context): Proxy? {
        val prefs = context.getSharedPreferences("vpn_status", Context.MODE_PRIVATE)
        val port = prefs.getInt("probe_port", 0)
        if (prefs.getString("status", null) != "connected" || port <= 0) return null
        return Proxy(Proxy.Type.HTTP, InetSocketAddress("127.0.0.1", port))
    }
}
