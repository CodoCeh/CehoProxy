package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class VpnSettingsActionSelectorTest {
    @Test fun prefersDedicatedVpnSettings() {
        assertEquals(VpnSettingsDestination.VPN, VpnSettingsActionSelector.select(true, true))
    }

    @Test fun fallsBackToGeneralSettingsOnTv() {
        assertEquals(VpnSettingsDestination.GENERAL, VpnSettingsActionSelector.select(false, true))
    }

    @Test fun reportsWhenNoSettingsActivityExists() {
        assertEquals(VpnSettingsDestination.NONE, VpnSettingsActionSelector.select(false, false))
    }
}
