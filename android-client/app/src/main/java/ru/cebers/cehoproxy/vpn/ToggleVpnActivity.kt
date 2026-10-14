package ru.cebers.cehoproxy.vpn

import android.app.Activity
import android.content.Intent
import android.net.VpnService
import android.os.Bundle
import androidx.core.content.ContextCompat

/** Starts or stops the tunnel from the home screen widget; shows no UI. */
class ToggleVpnActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val status = getSharedPreferences("vpn_status", MODE_PRIVATE).getString("status", "stopped")
        val running = CehoVpnService.hasLiveServiceInstance() && status != "stopped"
        when {
            running -> startService(Intent(this, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_STOP))
            VpnService.prepare(this) == null -> ContextCompat.startForegroundService(this, Intent(this, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_START))
            else -> startActivity(Intent(this, VpnPermissionActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        }
        finish()
    }
}
