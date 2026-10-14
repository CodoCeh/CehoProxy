package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class TunnelAppsPolicyTest {
    private val installed = listOf("a.app", "b.app", "c.app", "d.app", YoutubeAdPolicy.PACKAGE)

    @Test fun includeModeSkipsAppsThatGoDirectByDpi() {
        val profile = ClientProfile(apps = listOf("a.app", "b.app"), localDpiApps = listOf("b.app"))
        assertEquals(listOf("a.app"), TunnelAppsPolicy.packages(profile, installed))
    }

    @Test fun includeModeAddsPerAppNodeRoutes() {
        val profile = ClientProfile(apps = listOf("a.app"), appNodeRoutes = mapOf("c.app" to "route"))
        assertEquals(listOf("a.app", "c.app"), TunnelAppsPolicy.packages(profile, installed))
    }

    @Test fun excludeModeSkipsSelectedAndDpiAndAdBlockApps() {
        val profile = ClientProfile(apps = listOf("a.app"), localDpiApps = listOf("b.app"), excludeSelectedApps = true, blockYoutubeAds = true)
        assertEquals(listOf("c.app", "d.app"), TunnelAppsPolicy.packages(profile, installed))
    }
}
