package ru.cebers.cehoproxy.vpn

import org.junit.Assert.assertEquals
import org.junit.Test

class DirectReachabilityTest {
    @Test fun foreignSiteReachableMeansOpenInternet() = assertEquals(DirectReachability.State.OPEN, DirectReachability.classify(true, true))
    @Test fun onlyRussianSitesMeansCarrierRestriction() = assertEquals(DirectReachability.State.RESTRICTED, DirectReachability.classify(false, true))
    @Test fun nothingReachableMeansOffline() = assertEquals(DirectReachability.State.OFFLINE, DirectReachability.classify(false, false))
}
