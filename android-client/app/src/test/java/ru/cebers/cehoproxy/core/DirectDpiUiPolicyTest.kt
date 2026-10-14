package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class DirectDpiUiPolicyTest {
    @Test fun optInIncludesOnlyChosenAppAndRemovesItsProxyAssignment() {
        val old = ClientProfile(apps = listOf("other.app"), appNodeRoutes = mapOf("video.app" to "node"), sites = listOf("manual.example"))
        val next = DirectDpiUiPolicy.setMode(old, "video.app", true)
        assertEquals(listOf("other.app", "video.app"), next.apps)
        assertEquals(listOf("video.app"), next.localDpiApps)
        assertFalse(next.excludeSelectedApps)
        assertFalse(next.appNodeRoutes.containsKey("video.app"))
        assertEquals(old.sites, next.sites)
    }

    @Test fun optInRemovesOnlyChosenAppFromExclusions() {
        val old = ClientProfile(apps = listOf("video.app", "other.app"), excludeSelectedApps = true)
        val next = DirectDpiUiPolicy.setMode(old, "video.app", true)
        assertEquals(listOf("other.app"), next.apps)
        assertTrue(next.excludeSelectedApps)
    }

    @Test fun importedConfigCannotBeRewrittenByDpiMode() {
        val old = ClientProfile(importedConfig = "{}", apps = listOf("video.app"))
        assertEquals(old, DirectDpiUiPolicy.setMode(old, "video.app", true))
    }

    @Test fun switchingToProxyKeepsTheSelectedAppsScope() {
        val old = ClientProfile(apps = listOf("video.app"), localDpiApps = listOf("video.app"))
        val next = DirectDpiUiPolicy.setMode(old, "video.app", false)
        assertEquals(old.apps, next.apps)
        assertTrue(next.localDpiApps.isEmpty())
    }
}
