package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class VpnHomeActionTest {
    @Test fun allDeadLiveTunnelOffersStopWhileColdErrorOffersStart() {
        assertTrue(VpnHomeAction.shouldStop(false, true))
        assertTrue(VpnHomeAction.isBlocked("error", true))
        assertFalse(VpnHomeAction.shouldStop(false, false))
        assertFalse(VpnHomeAction.isBlocked("error", false))
        assertTrue(VpnHomeAction.shouldStop(true, true))
        assertFalse(VpnHomeAction.isBlocked("connected", true))
    }
}
