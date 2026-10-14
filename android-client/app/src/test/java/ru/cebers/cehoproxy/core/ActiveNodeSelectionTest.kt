package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class ActiveNodeSelectionTest {
    private val nodes = listOf(
        ProxyNode("old", "socks", "one.example", 1080, "old", subscriptionMetadata = SubscriptionMetadata(title = "Old subscription")),
        ProxyNode("backup", "socks", "two.example", 1080, "backup", subscriptionMetadata = SubscriptionMetadata(title = "Backup subscription")),
    )
    private val ui = ClientProfile(nodes = nodes, selected = 0, autoRotationEnabled = true, apps = listOf("app"))

    @Test fun statusUpdatesOnlyActiveSelectionAndUsesThatNodesMetadataAndCountry() {
        val stored = ui.copy(selected = 1, apps = listOf("different"))
        val refreshed = ActiveNodeSelection.sync(ui, stored)
        assertEquals(1, refreshed.selected)
        assertEquals(ui.apps, refreshed.apps)
        assertEquals("Подписка: Backup subscription", NodePresentation.subscriptionSource(refreshed.selectedNode!!))
        val countryByRoute = mapOf(nodes[0].routeId to VerifiedNodeCountry("DE", "8.8.8.8"), nodes[1].routeId to VerifiedNodeCountry("US", "1.1.1.1"))
        assertEquals("US", countryByRoute[refreshed.selectedNode!!.routeId]?.countryCode)
    }

    @Test fun settingsSaveCannotRevertFailoverButExplicitNodeChoiceWorks() {
        val stored = ui.copy(selected = 1)
        val pending = ui.copy(sites = listOf("edited.example"))
        val saved = ActiveNodeSelection.beforeSave(ui, pending, stored, false)
        assertEquals(1, saved.selected)
        assertEquals(pending.sites, saved.sites)
        assertEquals(0, ActiveNodeSelection.beforeSave(ui, pending, stored, true).selected)
        assertEquals(1, ActiveNodeSelection.beforeSave(ui, pending.copy(autoRotationEnabled = false), stored, false).selected)
    }

    @Test fun replacementProfileCannotBorrowOldSelection() {
        val replacement = ui.copy(nodes = nodes.map { it.copy(raw = it.raw + "new") }, selected = 1)
        assertEquals(ui, ActiveNodeSelection.sync(ui, replacement))
        assertEquals(ui, ActiveNodeSelection.sync(ui, ui.copy(selected = 1, autoRotationEnabled = false)))
    }
}
