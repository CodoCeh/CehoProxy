package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AppRoutePolicyTest {
    @Test fun excludeModeListsAppsKeptOutside() {
        val profile = ClientProfile(excludeSelectedApps = true, apps = listOf("ru.bank"))
        assertFalse(AppRoutePolicy.viaVpn(profile, "ru.bank"))
        assertTrue(AppRoutePolicy.viaVpn(profile, "org.telegram.messenger"))
        assertEquals(emptyList<String>(), AppRoutePolicy.setViaVpn(profile, "ru.bank", true).apps)
        assertEquals(listOf("ru.bank", "org.telegram.messenger"), AppRoutePolicy.setViaVpn(profile, "org.telegram.messenger", false).apps)
    }

    @Test fun selectedModeListsTunnelledApps() {
        val profile = ClientProfile(excludeSelectedApps = false, apps = listOf("org.telegram.messenger"), appNodeRoutes = mapOf("com.openai.chatgpt" to "node"))
        assertTrue(AppRoutePolicy.viaVpn(profile, "com.openai.chatgpt"))
        assertFalse(AppRoutePolicy.viaVpn(profile, "ru.bank"))
        assertEquals(listOf("org.telegram.messenger", "ru.bank"), AppRoutePolicy.setViaVpn(profile, "ru.bank", true).apps)
        val off = AppRoutePolicy.setViaVpn(profile, "com.openai.chatgpt", false)
        assertFalse(AppRoutePolicy.viaVpn(off, "com.openai.chatgpt"))
    }

    @Test fun repeatedChangeKeepsOneEntry() {
        val profile = ClientProfile(excludeSelectedApps = false)
        val twice = AppRoutePolicy.setViaVpn(AppRoutePolicy.setViaVpn(profile, "a", true), "a", true)
        assertEquals(listOf("a"), twice.apps)
    }
}
