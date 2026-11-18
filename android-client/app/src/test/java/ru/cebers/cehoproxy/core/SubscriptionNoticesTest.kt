package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SubscriptionNoticesTest {
    private val stubs = "vless://00000000-0000-0000-0000-000000000000@0.0.0.0:1?encryption=none&type=tcp&security=none#%F0%9F%9A%A7%20Subscription%20limited\n" +
        "vless://00000000-0000-0000-0000-000000000000@0.0.0.0:1?encryption=none&type=tcp&security=none#Contact%20support"
    private val real = "vless://11111111-1111-1111-1111-111111111111@198.51.100.10:443?security=tls&sni=a.example#DE"

    private fun sub(body: String, meta: SubscriptionMetadata = SubscriptionMetadata()) =
        meta.copy(sourceUrl = "https://panel.example/sub/x", sourceHost = "panel.example", title = "Мой VPN").attachTo(NodeShareParser.parse(body))

    @Test fun limitedSubscriptionShowsTheProviderText() {
        val notices = SubscriptionNotices.of(ClientProfile(nodes = sub(stubs)))
        assertEquals(1, notices.size)
        assertEquals("Подписка «Мой VPN» не работает", notices[0].title)
        assertTrue(notices[0].text, notices[0].text.contains("«🚧 Subscription limited · Contact support»"))
    }

    @Test fun usedUpTrafficIsShownEvenWithRealServers() {
        val notices = SubscriptionNotices.of(ClientProfile(nodes = sub(real, SubscriptionMetadata(uploadBytes = 1L shl 30, downloadBytes = 49L shl 30, totalBytes = 50L shl 30))))
        assertEquals(1, notices.size)
        assertTrue(notices[0].text, notices[0].text.startsWith("Трафик подписки закончился"))
    }

    @Test fun workingSubscriptionHasNoNotice() {
        assertEquals(emptyList<SubscriptionNotice>(), SubscriptionNotices.of(ClientProfile(nodes = sub(real, SubscriptionMetadata(uploadBytes = 1L shl 30, downloadBytes = 1L shl 30, totalBytes = 50L shl 30)))))
    }
}
