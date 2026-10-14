package ru.cebers.cehoproxy.core

internal object VpnHomeAction {
    fun shouldStop(connected: Boolean, liveServiceInstance: Boolean): Boolean = connected || liveServiceInstance
    fun isBlocked(status: String?, liveServiceInstance: Boolean): Boolean = status == "error" && liveServiceInstance
}
