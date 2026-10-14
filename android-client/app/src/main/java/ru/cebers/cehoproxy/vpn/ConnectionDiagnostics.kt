package ru.cebers.cehoproxy.vpn

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.URL

/** Step-by-step connection check for the "Проверить соединение" button. */
object ConnectionDiagnostics {
    data class Step(val title: String, val result: String, val ok: Boolean)

    fun run(context: Context): List<Step> {
        val connectivity = context.getSystemService(ConnectivityManager::class.java)
        val networks = connectivity.allNetworks.toList()
        fun caps(network: Network) = connectivity.getNetworkCapabilities(network)
        val activeCaps = connectivity.activeNetwork?.let(::caps)
        val hasInternet = activeCaps?.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) == true
        val physical = networks.firstOrNull { network ->
            caps(network)?.let { it.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) && !it.hasTransport(NetworkCapabilities.TRANSPORT_VPN) } == true
        }
        val status = context.getSharedPreferences("vpn_status", Context.MODE_PRIVATE).getString("status", "stopped")
        val probePort = context.getSharedPreferences("vpn_status", Context.MODE_PRIVATE).getInt("probe_port", 0)
        val viaVpn = if (status == "connected" && probePort > 0) reachableThroughProbe(probePort) else null
        val direct = physical?.let(::reachable)
        return listOf(
            Step("Сеть телефона", if (hasInternet) "Есть" else "Нет подключения к интернету", hasInternet),
            Step(
                "Туннель CehoProxy",
                when (status) { "connected" -> "Подключён"; "starting" -> "Запускается"; "error" -> "Ошибка"; else -> "Выключен" },
                status == "connected",
            ),
            Step(
                "Интернет через VPN",
                when (viaVpn) { null -> "VPN сейчас не включён"; true -> "Отвечает"; false -> "Не отвечает" },
                viaVpn == true,
            ),
            Step(
                "Интернет без VPN",
                when (direct) { null -> "Нет обычной сети"; true -> "Отвечает"; false -> "Не отвечает" },
                direct == true,
            ),
        )
    }

    private fun reachableThroughProbe(port: Int): Boolean = runCatching {
        val proxy = Proxy(Proxy.Type.HTTP, InetSocketAddress("127.0.0.1", port))
        val connection = URL("https://1.1.1.1/cdn-cgi/trace").openConnection(proxy) as HttpURLConnection
        try {
            connection.connectTimeout = 5000
            connection.readTimeout = 5000
            connection.instanceFollowRedirects = false
            connection.responseCode in 200..399
        } finally {
            connection.disconnect()
        }
    }.getOrDefault(false)

    private fun reachable(network: Network): Boolean = runCatching {
        val connection = network.openConnection(URL("https://1.1.1.1/cdn-cgi/trace")) as HttpURLConnection
        try {
            connection.connectTimeout = 5000
            connection.readTimeout = 5000
            connection.instanceFollowRedirects = false
            connection.responseCode in 200..399
        } finally {
            connection.disconnect()
        }
    }.getOrDefault(false)
}
