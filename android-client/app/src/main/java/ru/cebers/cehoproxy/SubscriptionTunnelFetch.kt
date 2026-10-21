package ru.cebers.cehoproxy

import android.content.Context
import ru.cebers.cehoproxy.core.FetchedSubscription
import ru.cebers.cehoproxy.core.SubscriptionFetcher
import ru.cebers.cehoproxy.core.SubscriptionRequestOptions
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Proxy

object SubscriptionTunnelFetch {
    fun fetch(context: Context, address: String, options: SubscriptionRequestOptions = SubscriptionRequestOptions()): FetchedSubscription =
        try {
            SubscriptionFetcher.fetchWithMetadata(address, options)
        } catch (direct: IOException) {
            val proxy = tunnelProxy(context) ?: throw direct
            SubscriptionFetcher.fetchWithMetadata(address, options.copy(proxy = proxy))
        }

    private fun tunnelProxy(context: Context): Proxy? {
        val prefs = context.getSharedPreferences("vpn_status", Context.MODE_PRIVATE)
        val port = prefs.getInt("probe_port", 0)
        if (prefs.getString("status", null) != "connected" || port <= 0) return null
        return Proxy(Proxy.Type.HTTP, InetSocketAddress("127.0.0.1", port))
    }
}
