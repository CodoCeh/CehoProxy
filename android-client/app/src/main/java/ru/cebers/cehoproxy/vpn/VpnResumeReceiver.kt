package ru.cebers.cehoproxy.vpn

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.net.VpnService
import androidx.core.content.ContextCompat

/** Brings the VPN back after a reboot or an app update if the user had left it on. */
class VpnResumeReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_BOOT_COMPLETED && intent.action != Intent.ACTION_MY_PACKAGE_REPLACED) return
        if (!CehoVpnService.wantsVpn(context) || VpnService.prepare(context) != null) return
        try {
            ContextCompat.startForegroundService(context, Intent(context, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_START).putExtra(CehoVpnService.EXTRA_RESUME, true))
        } catch (_: Exception) {
            CehoVpnService.notifyProblem(context, "VPN выключился. Откройте приложение и включите снова")
        }
    }
}
