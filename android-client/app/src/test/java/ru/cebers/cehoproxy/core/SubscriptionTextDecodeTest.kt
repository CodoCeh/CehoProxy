package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SubscriptionTextDecodeTest {
    @Test fun base64PrefixedAnnouncementIsDecoded() {
        val encoded = "base64:" + java.util.Base64.getEncoder().encodeToString("💰 Приглашай друзей".toByteArray())
        assertEquals("💰 Приглашай друзей", decodeBase64Text(encoded))
    }

    @Test fun plainTextAndBrokenPayloadStayAsIs() {
        assertEquals("Обычный текст", decodeBase64Text("Обычный текст"))
        assertNull(decodeBase64Text("base64:%%%"))
    }
}
