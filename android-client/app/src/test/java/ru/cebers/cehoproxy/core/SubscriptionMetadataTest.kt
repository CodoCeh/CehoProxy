package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SubscriptionMetadataTest {
    @Test fun readsExpiryFromStandardSubscriptionUserInfo() {
        assertEquals(1_800_000_000L, SubscriptionMetadata.fromHeader("upload=10; download=20; total=100; expire=1800000000").expiresAtEpochSeconds)
        assertEquals(1_800_000_000L, SubscriptionMetadata.fromHeader("upload=10, expire=1800000000").expiresAtEpochSeconds)
    }

    @Test fun ignoresMissingMalformedAndOutOfRangeExpiry() {
        assertNull(SubscriptionMetadata.fromHeader(null).expiresAtEpochSeconds)
        assertNull(SubscriptionMetadata.fromHeader("upload=10; expire=never").expiresAtEpochSeconds)
        assertNull(SubscriptionMetadata.fromHeader("expire=9223372036854775807").expiresAtEpochSeconds)
    }

    @Test fun readsHappMetadataFromSubscriptionComments() {
        val metadata = SubscriptionMetadata.fromBody("""
            #profile-title: Ceh VPN
            #subscription-userinfo: upload=1024; download=2048; total=4096; expire=1800000000
            #sub-info-text: Акция продления
            #support-url: https://t.me/example_support
            #profile-web-page-url: https://example.org/account
            #sub-info-button-text: Кабинет
            #sub-info-button-link: https://example.org/account
            #sub-expire-button-link: tg://resolve?domain=example_bot
            #notification-subs-expire: 1
            vless://user@node.example:443
        """.trimIndent())
        assertEquals("Ceh VPN", metadata.title)
        assertEquals(3_072L, metadata.usedBytes)
        assertEquals(4_096L, metadata.totalBytes)
        assertEquals("Акция продления", metadata.infoText)
        assertEquals("https://t.me/example_support", metadata.supportUrl)
        assertEquals("https://example.org/account", metadata.webPageUrl)
        assertEquals("Кабинет", metadata.infoButtonText)
        assertEquals("tg://resolve?domain=example_bot", metadata.renewUrl)
        assertEquals(true, metadata.notifyOnExpiry)
    }

    @Test fun titleInsideBase64SubscriptionIsPreserved() {
        val body = "#profile-title: Body plan\n#subscription-userinfo: expire=1800000000\nvless://synthetic@edge.example:443"
        val encoded = java.util.Base64.getEncoder().encodeToString(body.toByteArray())
        val metadata = SubscriptionMetadata.fromBody(encoded)
        assertEquals("Body plan", metadata.title)
        assertEquals(1_800_000_000L, metadata.expiresAtEpochSeconds)
    }

    @Test fun encodedTitleIsDecodedBeforeDisplayLimitAndMalformedTitleIsUnknown() {
        val title = "Название подписки ".repeat(10)
        val encoded = "base64:" + java.util.Base64.getEncoder().encodeToString(title.toByteArray())
        assertEquals(title.trim().take(80), SubscriptionMetadata.fromValues(mapOf("profile-title" to encoded)).title)
        assertNull(SubscriptionMetadata.fromValues(mapOf("profile-title" to "base64:%%%invalid")).title)
        assertNull(SubscriptionMetadata.fromValues(mapOf("profile-title" to "base64:" + "A".repeat(5000))).title)
    }

    @Test fun attachmentPrefersFetchedMetadataAndRetainsMissingNodeFields() {
        val original = ProxyNode("node", "vless", "edge.example", 443, "synthetic", subscriptionExpiresAt = 1_800_000_000L,
            subscriptionMetadata = SubscriptionMetadata(title = "Previous plan", supportUrl = "https://example.org/support"))
        val attached = SubscriptionMetadata(title = "Fresh plan").attachTo(listOf(original)).single()
        assertEquals("Fresh plan", attached.subscriptionMetadata.title)
        assertEquals(original.subscriptionMetadata.supportUrl, attached.subscriptionMetadata.supportUrl)
        assertEquals(original.subscriptionExpiresAt, attached.subscriptionExpiresAt)
        assertEquals(original.raw, attached.raw)
    }

    @Test fun rejectsUnsafeProviderLinksAndCapsDisplayText() {
        val metadata = SubscriptionMetadata.fromValues(mapOf("support-url" to "javascript:alert(1)", "sub-info-text" to "x".repeat(500)))
        assertNull(metadata.supportUrl)
        assertEquals(200, metadata.infoText?.length)
    }
}
