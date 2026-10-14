package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class ProfileBackupTest {
    private val sourceNode = ProxyNode("node", "vless", "node.example", 443, "vless://secret@node.example:443")
    private val source = ClientProfile(
        nodes = listOf(sourceNode),
        selected = 0,
        apps = listOf("com.example.vpn"),
        sites = listOf("example.org"),
        onlySites = true,
        importedConfig = "{\"outbounds\":[]}",
        excludeSelectedApps = true,
        appNodeRoutes = mapOf("com.example.vpn" to sourceNode.routeId),
        siteNodeRoutes = mapOf("example.org" to sourceNode.routeId),
    )

    @Test fun backupRoundTripRestoresAllProfileFields() {
        val original = ClientProfile(
            nodes = listOf(
                ProxyNode("one", "vless", "one.example", 443, "vless://id@one.example:443"),
                ProxyNode("two", "trojan", "two.example", 8443, "trojan://secret@two.example:8443"),
            ),
            selected = 1,
            apps = listOf("com.example.app"),
            sites = listOf("example.org", "docs.example.org"),
            onlySites = true,
            importedConfig = "{\"outbounds\":[]}",
            excludeSelectedApps = true,
        )

        assertEquals(original, ProfileBackup.decode(ProfileBackup.encode(original)))
    }

    @Test fun backupWithoutImportedConfigRestoresNodesAndPolicies() {
        val original = ClientProfile(
            nodes = listOf(ProxyNode("node", "vless", "node.example", 443, "vless://id@node.example:443")),
            selected = 0,
            apps = listOf("com.example.app"),
            sites = listOf("example.org"),
            onlySites = false,
        )

        assertEquals(original, ProfileBackup.decode(ProfileBackup.encode(original)))
    }

    @Test fun unrelatedJsonIsNotTreatedAsBackup() {
        assertNull(ProfileBackup.decode("{\"outbounds\":[]}"))
    }

    @Test fun malformedBackupIsRejected() {
        assertNull(ProfileBackup.decode("not json"))
    }

    @Test fun settingsExportOmitsSubscriptionsAndRestorePreservesDestinationNodes() {
        val destination = ClientProfile(nodes = listOf(ProxyNode("existing", "trojan", "existing.example", 443, "trojan://old@existing.example:443")))
        val exported = ProfileBackup.encode(source, ProfileExportScope.SETTINGS)
        assertEquals(emptyList<ProxyNode>(), ProfileBackup.decode(exported)?.nodes)
        assertEquals(false, exported.contains(sourceNode.raw))
        assertEquals(source.appNodeRoutes, ProfileBackup.decode(exported)?.appNodeRoutes)
        assertEquals(source.siteNodeRoutes, ProfileBackup.decode(exported)?.siteNodeRoutes)
        val restored = ProfileBackup.restore(destination, exported)
        assertEquals(destination.nodes, restored?.nodes)
        assertEquals(source.apps, restored?.apps)
        assertEquals(source.sites, restored?.sites)
        assertEquals(source.importedConfig, restored?.importedConfig)
    }

    @Test fun subscriptionsExportOmitsSettingsAndRestorePreservesDestinationRules() {
        val destination = ClientProfile(apps = listOf("com.destination"), sites = listOf("destination.example"), onlySites = true)
        val exported = ProfileBackup.encode(source, ProfileExportScope.SUBSCRIPTIONS)
        assertEquals(emptyList<String>(), ProfileBackup.decode(exported)?.apps)
        assertNull(ProfileBackup.decode(exported)?.importedConfig)
        val restored = ProfileBackup.restore(destination, exported)
        assertEquals(source.nodes, restored?.nodes)
        assertEquals(destination.apps, restored?.apps)
        assertEquals(destination.sites, restored?.sites)
        assertEquals(destination.onlySites, restored?.onlySites)
    }

    @Test fun fullBackupRestoreReplacesEntireProfile() {
        val destination = ClientProfile(apps = listOf("com.destination"))
        assertEquals(source, ProfileBackup.restore(destination, ProfileBackup.encode(source)))
    }
}
