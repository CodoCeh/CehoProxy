package ru.cebers.cehoproxy.vpn

import android.app.Activity
import android.content.Intent
import android.net.VpnService
import android.os.Bundle
import android.widget.Toast
import androidx.core.content.ContextCompat
import ru.cebers.cehoproxy.core.NodePresentation
import ru.cebers.cehoproxy.core.ProfileStore
import ru.cebers.cehoproxy.nodeAllowed

/** Switches to the next allowed server from the home screen widget and reconnects; shows no UI. */
class NextServerActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val store = ProfileStore(this)
        val profile = store.read()
        val next = WidgetServerChoice.next(profile.nodes.indices.filter { nodeAllowed(profile, profile.nodes[it]) }, profile.selected)
        if (profile.importedConfig != null || next == null) {
            Toast.makeText(this, "Других серверов нет", Toast.LENGTH_SHORT).show()
            finish()
            return
        }
        store.update { current -> if (current.nodes.map { it.raw } == profile.nodes.map { it.raw }) current.copy(selected = next) else current }
        Toast.makeText(this, "Сервер: ${NodePresentation.routeName(profile.nodes[next])}", Toast.LENGTH_SHORT).show()
        val status = getSharedPreferences("vpn_status", MODE_PRIVATE).getString("status", "stopped")
        if (CehoVpnService.hasLiveServiceInstance() && status != "stopped" && VpnService.prepare(this) == null)
            ContextCompat.startForegroundService(this, Intent(this, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_START))
        finish()
    }
}

internal object WidgetServerChoice {
    fun next(allowed: List<Int>, selected: Int): Int? {
        if (allowed.isEmpty() || (allowed.size == 1 && allowed[0] == selected)) return null
        return allowed.firstOrNull { it > selected } ?: allowed.first()
    }
}
