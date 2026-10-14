package ru.cebers.cehoproxy.core

import androidx.test.platform.app.InstrumentationRegistry
import io.nekohasekai.libbox.Libbox
import org.junit.Assert.*
import org.junit.Test
import ru.cebers.cehoproxy.vpn.AndroidSystemTrustBundle
import ru.cebers.cehoproxy.vpn.CountryRuleSetCache
import java.io.File

class CountryRuleSetEngineTest {
    @Test fun allBundledCountrySetsAreReadableByNativeEngineWithBothSiteModes() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val node = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443#country-test").single()
        val backup = NodeShareParser.parse("vless://22222222-2222-2222-2222-222222222222@backup.example:443#backup").single()
        for (country in CountryRouting.countries) {
            val sets = CountryRuleSetCache.prepare(context, country.code)
            assertEquals(if (country.domainAsset == null) 1 else 2, sets.size)
            assertTrue(sets.all { File(it.path).isFile && it.path.startsWith(context.filesDir.absolutePath) })
            val originalBytes = sets.associate { it.path to File(it.path).readBytes().toList() }
            assertEquals(sets, CountryRuleSetCache.prepare(context, country.code))
            assertEquals(originalBytes, sets.associate { it.path to File(it.path).readBytes().toList() })
            for (mode in SiteMode.entries) {
                val config = RoutingConfig.build(listOf(node, backup), 0, RoutePolicy(apps = listOf("com.android.chrome"),
                    sites = listOf("manual.example"), siteNodeRoutes = mapOf("assigned.example" to backup.routeId), siteMode = mode, countryRuleSets = sets))
                Libbox.checkConfig(AndroidSystemTrustBundle.applyTo(context, config))
            }
        }
    }

    @Test fun corruptedCachedSetIsRestoredFromVerifiedBundledAsset() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val set = CountryRuleSetCache.prepare(context, "ru").first()
        val file = File(set.path)
        val original = file.readBytes()
        try {
            file.writeText("corrupt cache")
            CountryRuleSetCache.prepare(context, "ru")
            assertArrayEquals(original, file.readBytes())
        } finally {
            file.writeBytes(original)
        }
        assertTrue(CountryRuleSetCache.prepare(context, "").isEmpty())
    }
}
