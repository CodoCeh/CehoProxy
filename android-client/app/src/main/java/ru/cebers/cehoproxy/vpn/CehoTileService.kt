package ru.cebers.cehoproxy.vpn

import android.annotation.SuppressLint
import android.animation.ValueAnimator
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.drawable.Icon
import android.net.VpnService
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import android.util.Log
import androidx.core.content.ContextCompat
import ru.cebers.cehoproxy.BuildConfig
import ru.cebers.cehoproxy.R

class CehoTileService : TileService() {
    private var listening = false
    private val animationHandler = Handler(Looper.getMainLooper())
    private var animationScheduled = false
    private val animationPolicy = TileAnimationPolicy()
    private val animate = Runnable {
        animationScheduled = false
        updateTile()
    }
    private val statusReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            when (intent?.getStringExtra("status")) {
                "stopped" -> { animationPolicy.resetActivation(); stopAnimation() }
                "error" -> { animationPolicy.cancelWindow(); stopAnimation() }
            }
            updateTile()
        }
    }

    override fun onStartListening() {
        super.onStartListening()
        if (BuildConfig.DEBUG) Log.d("CehoTile", "onStartListening")
        if (!listening) {
            ContextCompat.registerReceiver(this, statusReceiver, IntentFilter(CehoVpnService.ACTION_STATUS), ContextCompat.RECEIVER_NOT_EXPORTED)
            listening = true
        }
        updateTile()
    }

    override fun onStopListening() {
        if (BuildConfig.DEBUG) Log.d("CehoTile", "onStopListening")
        if (listening) unregisterReceiver(statusReceiver)
        listening = false
        animationPolicy.cancelWindow()
        stopAnimation()
        super.onStopListening()
    }

    override fun onDestroy() {
        if (BuildConfig.DEBUG) Log.d("CehoTile", "onDestroy")
        if (listening) unregisterReceiver(statusReceiver)
        listening = false
        animationPolicy.cancelWindow()
        stopAnimation()
        super.onDestroy()
    }

    private fun stopAnimation() {
        animationHandler.removeCallbacks(animate)
        animationScheduled = false
    }

    @SuppressLint("StartActivityAndCollapseDeprecated")
    override fun onClick() {
        super.onClick()
        unlockAndRun {
            val storedStatus = getSharedPreferences("vpn_status", MODE_PRIVATE).getString("status", "stopped")
            if (CehoVpnService.hasLiveServiceInstance() && storedStatus != "stopped") {
                animationPolicy.resetActivation()
                stopAnimation()
                startService(Intent(this, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_STOP))
            } else {
                animationPolicy.resetActivation()
                stopAnimation()
                val started = animationPolicy.beginActivation(SystemClock.elapsedRealtime(), ValueAnimator.areAnimatorsEnabled())
                if (BuildConfig.DEBUG) Log.d("CehoTile", "activationWindowStarted=$started durationMs=2000")
                if (VpnService.prepare(this) == null) {
                    ContextCompat.startForegroundService(this, Intent(this, CehoVpnService::class.java).setAction(CehoVpnService.ACTION_START))
                } else {
                    val intent = Intent(this, VpnPermissionActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    if (Build.VERSION.SDK_INT >= 34) startActivityAndCollapse(PendingIntent.getActivity(this, 0, intent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE))
                    else @Suppress("DEPRECATION") startActivityAndCollapse(intent)
                }
            }
            updateTile()
        }
    }

    private fun updateTile() {
        val tile = qsTile ?: return
        val stored = getSharedPreferences("vpn_status", MODE_PRIVATE).getString("status", "stopped")
        val live = CehoVpnService.hasLiveServiceInstance()
        val status = if (live) stored else "stopped"
        if (status == "error" || !listening || !ValueAnimator.areAnimatorsEnabled()) animationPolicy.cancelWindow()
        val now = SystemClock.elapsedRealtime()
        val remaining = animationPolicy.remainingMillis(now)
        val animateNow = listening && remaining != null
        if (animateNow && !animationPolicy.takeAnimationPublication(now)) return
        if (!animateNow) stopAnimation()
        tile.label = "CehoProxy"
        // One publication lets system UI own the finite Animatable2 timeline.
        tile.icon = Icon.createWithResource(this, if (animateNow) R.drawable.ic_ceho_whirlpool_activation else R.drawable.ic_ceho_whirlpool)
        tile.state = if ((live && status != "stopped") || animateNow) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
        val displayStatus = if (animateNow && !live) "starting" else status
        tile.subtitle = when (displayStatus) {
            "starting" -> "Подключаемся"
            "connected" -> "Подключено"
            "error" -> "Ошибка подключения"
            else -> "Выключено"
        }
        tile.contentDescription = "CehoProxy: ${tile.subtitle}"
        tile.updateTile()
        if (BuildConfig.DEBUG) {
            val safeStatus = status?.takeIf { it in setOf("starting", "connected", "error", "stopped") } ?: "unknown"
            Log.d("CehoTile", "published icon=${if (animateNow) "animated" else "static"} live=$live status=$safeStatus windowRemainingMs=${remaining ?: 0}")
        }
        if (animateNow && !animationScheduled) {
            animationScheduled = true
            animationHandler.postDelayed(animate, remaining!!)
        }
    }
}
