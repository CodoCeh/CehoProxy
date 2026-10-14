package ru.cebers.cehoproxy.core

import io.nekohasekai.libbox.Libbox
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import ru.cebers.cehoproxy.vpn.AndroidSystemTrustBundle
import java.security.cert.CertificateFactory
import org.junit.Test

class RoutingEngineConfigTest {
    @Test fun nativeEngineAcceptsOrderedSelectorsAndPrivateProbe() {
        val nodes = NodeShareParser.parse("socks5://127.0.0.1:10001#Dead\nsocks5://127.0.0.1:10002#Backup")
        Libbox.checkConfig(OrderedFailoverConfig.withProbe(RoutingConfig.build(nodes, 1, RoutePolicy(autoRotationEnabled = true)), 19001))
        Libbox.checkConfig(OrderedFailoverConfig.withProbe(RoutingConfig.build(nodes.take(1), 0, RoutePolicy()), 19002))
    }

    @Test fun nativeEngineAcceptsNonemptyAndroidSystemTrustBundle() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val bundle = AndroidSystemTrustBundle.export(context)
        val parsed = bundle.file.inputStream().use { CertificateFactory.getInstance("X.509").generateCertificates(it) }
        assertTrue(bundle.certificateCount > 0)
        assertEquals(bundle.certificateCount, parsed.size)
        val node = NodeShareParser.parse("hysteria2://synthetic-pass@edge.example:443?sni=edge.example#Trust-test").single()
        val config = AndroidSystemTrustBundle.applyTo(context, RoutingConfig.build(node, RoutePolicy()))
        Libbox.checkConfig(config)
    }

    @Test fun nativeEngineAcceptsPhysicalBootstrapDnsAndBothSiteDirections() {
        val node = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443#DNS-test").single()
        for (mode in SiteMode.entries) {
            Libbox.checkConfig(RoutingConfig.build(node, RoutePolicy(
                apps = listOf("com.android.chrome"),
                sites = listOf("api.ipify.org"),
                siteMode = mode,
            )))
        }
    }
    @Test fun nativeEngineAcceptsPerAppDpiWithAndWithoutSubscription() {
        val node = NodeShareParser.parse("vless://synthetic-id@edge.example:443#DPI-test").single()
        for (nodes in listOf(listOf(node), emptyList())) {
            for (mode in SiteMode.entries) {
                Libbox.checkConfig(RoutingConfig.build(nodes, 0, RoutePolicy(
                    apps = listOf("com.google.android.youtube"),
                    localDpiApps = listOf("com.google.android.youtube"),
                    sites = listOf("manual.example"),
                    siteMode = mode,
                )))
            }
        }
    }
}
