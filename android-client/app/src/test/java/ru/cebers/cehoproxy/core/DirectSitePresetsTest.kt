package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class DirectSitePresetsTest {
    @Test fun presetsMatchDesktopLists() {
        assertEquals(18, DirectSitePresets.tunnelSites.size)
        assertEquals(244, DirectSitePresets.russianSites.size)
        assertTrue("gosuslugi.ru" in DirectSitePresets.russianSites)
        assertTrue("youtube.com" in DirectSitePresets.tunnelSites)
    }

    @Test fun modePicksMatchingPreset() {
        assertEquals(DirectSitePresets.russianSites, DirectSitePresets.forMode(onlySites = false))
        assertEquals(DirectSitePresets.tunnelSites, DirectSitePresets.forMode(onlySites = true))
    }

    @Test fun newHostsSkipsListedSitesInAnyWrittenForm() {
        val listed = listOf("https://VK.com/", "gosuslugi.ru")
        val fresh = DirectSitePresets.newHosts(listed, listOf("vk.com", "gosuslugi.ru", "yandex.ru", "yandex.ru"))
        assertEquals(listOf("yandex.ru"), fresh)
    }

    @Test fun newHostsOnFullListIsEmpty() {
        assertTrue(DirectSitePresets.newHosts(DirectSitePresets.tunnelSites, DirectSitePresets.tunnelSites).isEmpty())
    }
}
