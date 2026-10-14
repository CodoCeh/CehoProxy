package ru.cebers.cehoproxy.vpn

import kotlin.test.Test
import kotlin.test.assertEquals

class NetworkMonitorStrategyTest {
    @Test
    fun `uses best matching callback on Android 12 and newer`() {
        assertEquals(NetworkMonitorStrategy.BEST_MATCHING, NetworkMonitorStrategy.forApi(31))
        assertEquals(NetworkMonitorStrategy.BEST_MATCHING, NetworkMonitorStrategy.forApi(36))
    }

    @Test
    fun `requests a matching network on Android 9 through 11`() {
        assertEquals(NetworkMonitorStrategy.REQUEST, NetworkMonitorStrategy.forApi(28))
        assertEquals(NetworkMonitorStrategy.REQUEST, NetworkMonitorStrategy.forApi(30))
    }

    @Test
    fun `uses default callback on Android 7 and 8`() {
        assertEquals(NetworkMonitorStrategy.DEFAULT, NetworkMonitorStrategy.forApi(24))
        assertEquals(NetworkMonitorStrategy.DEFAULT, NetworkMonitorStrategy.forApi(25))
        assertEquals(NetworkMonitorStrategy.DEFAULT_WITH_HANDLER, NetworkMonitorStrategy.forApi(26))
        assertEquals(NetworkMonitorStrategy.DEFAULT_WITH_HANDLER, NetworkMonitorStrategy.forApi(27))
    }
}
