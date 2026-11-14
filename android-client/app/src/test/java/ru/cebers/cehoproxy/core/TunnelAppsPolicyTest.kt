package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class TunnelAppsPolicyTest {
    private val installed = listOf("a.app", "b.app", "c.app", "d.app", "e.app")

    @Test fun includeModeSkipsAppsThatGoDirectByDpi() {
        val profile = ClientProfile(apps = listOf("a.app", "b.app"), localDpiApps = listOf("b.app"))
        assertEquals(listOf("a.app"), TunnelAppsPolicy.packages(profile, installed))
    }

    @Test fun includeModeAddsPerAppNodeRoutes() {
        val profile = ClientProfile(apps = listOf("a.app"), appNodeRoutes = mapOf("c.app" to "route"))
        assertEquals(listOf("a.app", "c.app"), TunnelAppsPolicy.packages(profile, installed))
    }

    @Test fun excludeModeSkipsSelectedAndDpiApps() {
        val profile = ClientProfile(apps = listOf("a.app"), localDpiApps = listOf("b.app"), excludeSelectedApps = true)
        assertEquals(listOf("c.app", "d.app", "e.app"), TunnelAppsPolicy.packages(profile, installed))
    }
}
