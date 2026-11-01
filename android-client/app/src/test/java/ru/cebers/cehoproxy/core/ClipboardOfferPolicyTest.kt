package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ClipboardOfferPolicyTest {
    private val empty = ClientProfile()

    @Test fun offersSubscriptionLinks() {
        assertEquals("https://example.com/sub/abc", ClipboardOfferPolicy.candidate(" https://example.com/sub/abc ", empty))
        assertEquals("https://example.com/k3J9xQ2mN8vB4zL7", ClipboardOfferPolicy.candidate("https://example.com/k3J9xQ2mN8vB4zL7", empty))
        assertEquals("https://sub.example.com/x", ClipboardOfferPolicy.candidate("https://sub.example.com/x", empty))
        assertEquals("https://panel.example/api?token=1", ClipboardOfferPolicy.candidate("https://panel.example/api?token=1", empty))
    }

    @Test fun unwrapsProviderAppLinks() {
        assertEquals("https://example.com/plain", ClipboardOfferPolicy.candidate("happ://add/https://example.com/plain", empty))
    }

    @Test fun offersServerLinks() {
        val vless = "vless://11111111-1111-1111-1111-111111111111@edge.example:443?security=tls#Test"
        assertEquals(vless, ClipboardOfferPolicy.candidate(vless, empty))
    }

    @Test fun ignoresOrdinaryTextAndPages() {
        assertNull(ClipboardOfferPolicy.candidate("привет, как дела", empty))
        assertNull(ClipboardOfferPolicy.candidate("https://ya.ru/news/story", empty))
        assertNull(ClipboardOfferPolicy.candidate("", empty))
        assertNull(ClipboardOfferPolicy.candidate(null, empty))
    }

    @Test fun ignoresWhatTheProfileAlreadyHas() {
        assertNull(ClipboardOfferPolicy.candidate("https://example.com/sub/abc", ClientProfile(subscriptionUrls = listOf("https://example.com/sub/abc"))))
        assertNull(ClipboardOfferPolicy.candidate("HTTPS://Example.com/sub/abc/#CebCeh", ClientProfile(subscriptionUrls = listOf("https://example.com/sub/abc"))))
        val vless = "vless://11111111-1111-1111-1111-111111111111@edge.example:443?security=tls#Test"
        assertNull(ClipboardOfferPolicy.candidate(vless, ClientProfile(nodes = NodeShareParser.parse(vless))))
    }
}
