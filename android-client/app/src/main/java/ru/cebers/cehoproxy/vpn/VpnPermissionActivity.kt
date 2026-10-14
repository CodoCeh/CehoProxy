package ru.cebers.cehoproxy.vpn

import android.app.Activity
import android.content.Intent
import android.net.VpnService
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat

/** System VPN consent needs a visible Activity, including starts from Quick Settings. */
class VpnPermissionActivity : ComponentActivity() {
    private val permission = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        if (result.resultCode == Activity.RESULT_OK && VpnService.prepare(this) == null) startVpn()
        finish()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (savedInstanceState != null) return
        val consent = VpnService.prepare(this)
        if (consent == null) { startVpn(); finish() } else permission.launch(consent)
    }

    private fun startVpn() {
        ContextCompat.startForegroundService(this, Intent(this, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_START))
    }
}
