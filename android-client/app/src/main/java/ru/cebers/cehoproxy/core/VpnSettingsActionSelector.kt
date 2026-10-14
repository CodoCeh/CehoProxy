package ru.cebers.cehoproxy.core

enum class VpnSettingsDestination { VPN, GENERAL, NONE }

object VpnSettingsActionSelector {
    fun select(vpnSettingsAvailable: Boolean, generalSettingsAvailable: Boolean): VpnSettingsDestination = when {
        vpnSettingsAvailable -> VpnSettingsDestination.VPN
        generalSettingsAvailable -> VpnSettingsDestination.GENERAL
        else -> VpnSettingsDestination.NONE
    }
}
