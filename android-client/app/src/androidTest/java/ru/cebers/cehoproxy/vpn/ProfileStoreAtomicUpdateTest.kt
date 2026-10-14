package ru.cebers.cehoproxy.vpn

import android.content.Context
import android.content.ContextWrapper
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.NodeShareParser
import ru.cebers.cehoproxy.core.ProfileStore
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class ProfileStoreAtomicUpdateTest {
    private fun isolatedContext(): Context = object : ContextWrapper(InstrumentationRegistry.getInstrumentation().targetContext) {
        override fun getSharedPreferences(name: String, mode: Int) = super.getSharedPreferences("atomic_update_fixture_$name", mode)
    }
    private fun profile(): ClientProfile = ClientProfile(
        nodes = NodeShareParser.parse("socks5://127.0.0.1:10001#First\nsocks5://127.0.0.1:10002#Second"),
        autoRotationEnabled = true,
    )

    @Test fun staleFailoverCannotResurrectAutoOrEraseNewRoutesFromAnotherStoreInstance() {
        val context = isolatedContext()
        val vpnStore = ProfileStore(context)
        val uiStore = ProfileStore(context)
        val started = profile()
        vpnStore.save(started)
        val staleRead = CountDownLatch(1)
        val uiSaved = CountDownLatch(1)
        val executor = Executors.newFixedThreadPool(2)
        try {
            val vpn = executor.submit<ClientProfile> {
                val stale = vpnStore.read()
                staleRead.countDown()
                check(uiSaved.await(3, TimeUnit.SECONDS))
                vpnStore.update { current -> FailoverSelection.apply(current, stale, stale.nodes[1].raw, true) }
            }
            val ui = executor.submit<ClientProfile> {
                check(staleRead.await(3, TimeUnit.SECONDS))
                uiStore.update { it.copy(autoRotationEnabled = false, appNodeRoutes = mapOf("app.new" to it.nodes[0].routeId), sites = listOf("new.example")) }
                    .also { uiSaved.countDown() }
            }
            val expected = ui.get(5, TimeUnit.SECONDS)
            assertEquals(expected, vpn.get(5, TimeUnit.SECONDS))
            assertEquals(expected, vpnStore.read())
            assertFalse(vpnStore.read().autoRotationEnabled)
        } finally { uiSaved.countDown(); executor.shutdownNow() }
    }

    @Test fun transformAndWriteHoldOneSharedLockAcrossStoreInstances() {
        val context = isolatedContext()
        val vpnStore = ProfileStore(context)
        val uiStore = ProfileStore(context)
        val started = profile()
        vpnStore.save(started)
        val vpnInside = CountDownLatch(1)
        val releaseVpn = CountDownLatch(1)
        val uiAttempting = CountDownLatch(1)
        val uiInside = CountDownLatch(1)
        val executor = Executors.newFixedThreadPool(2)
        try {
            val vpn = executor.submit<ClientProfile> {
                vpnStore.update { current ->
                    vpnInside.countDown()
                    check(releaseVpn.await(3, TimeUnit.SECONDS))
                    FailoverSelection.apply(current, started, started.nodes[1].raw, true)
                }
            }
            check(vpnInside.await(3, TimeUnit.SECONDS))
            val ui = executor.submit<ClientProfile> {
                uiAttempting.countDown()
                uiStore.update { current -> uiInside.countDown(); current.copy(autoRotationEnabled = false, sites = listOf("new.example")) }
            }
            check(uiAttempting.await(3, TimeUnit.SECONDS))
            assertFalse("Second transform cannot enter while first owns the lock", uiInside.await(100, TimeUnit.MILLISECONDS))
            releaseVpn.countDown()
            assertEquals(1, vpn.get(5, TimeUnit.SECONDS).selected)
            val final = ui.get(5, TimeUnit.SECONDS)
            assertEquals(1, final.selected)
            assertFalse(final.autoRotationEnabled)
            assertEquals(listOf("new.example"), final.sites)
            assertEquals(final, vpnStore.read())
        } finally { releaseVpn.countDown(); executor.shutdownNow() }
    }
}
