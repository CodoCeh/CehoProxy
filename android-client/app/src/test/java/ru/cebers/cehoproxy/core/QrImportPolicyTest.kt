package ru.cebers.cehoproxy.core

import kotlin.test.Test
import kotlin.test.assertEquals

class QrImportPolicyTest {
    @Test fun acceptsRemoteSubscriptionUrl() {
        assertEquals(
            QrImportDecision.Import("https://vpn.example/sub/abc"),
            QrImportPolicy.decide(listOf(" https://vpn.example/sub/abc ")),
        )
    }

    @Test fun acceptsSupportedShareLink() {
        val link = "vless://123e4567-e89b-12d3-a456-426614174000@node.example:443#primary"
        assertEquals(QrImportDecision.Import(link), QrImportPolicy.decide(listOf(link)))
    }

    @Test fun rejectsEmptyAndUnsupportedPayloads() {
        assertEquals(QrImportDecision.NoCode, QrImportPolicy.decide(listOf(null, "  ")))
        assertEquals(QrImportDecision.Unsupported, QrImportPolicy.decide(listOf("just some text")))
        assertEquals(QrImportDecision.Unsupported, QrImportPolicy.decide(listOf("https:///missing-host")))
    }

    @Test fun rejectsImagesWithMoreThanOneDistinctQrValue() {
        assertEquals(
            QrImportDecision.MultipleCodes,
            QrImportPolicy.decide(listOf("https://vpn.example/a", "vless://id@node.example:443")),
        )
    }

    @Test fun duplicateDetectionOfSameCodeRemainsUnambiguous() {
        val link = "https://vpn.example/sub/abc"
        assertEquals(QrImportDecision.Import(link), QrImportPolicy.decide(listOf(link, link)))
    }
}
