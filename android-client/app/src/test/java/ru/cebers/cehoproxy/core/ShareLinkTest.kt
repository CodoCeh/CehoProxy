package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ShareLinkTest {
    private val token = "0123456789abcdef0123456789abcdef"
    private val url = ShareLink.url("192.168.1.20", 41234, token)

    @Test fun recognisesOnlyPairingAddresses() {
        assertTrue(ShareLink.isPairingUrl(url))
        assertFalse(ShareLink.isPairingUrl("https://192.168.1.20:41234/p/$token"))
        assertFalse(ShareLink.isPairingUrl("http://192.168.1.20:41234/p/short"))
        assertFalse(ShareLink.isPairingUrl("http://example.com/sub/abc"))
        assertFalse(ShareLink.isPairingUrl("$url?x=1"))
    }

    @Test fun appLinkRoundTrips() {
        assertEquals(url, ShareLink.fromAppLink(ShareLink.appLink(url)))
        assertNull(ShareLink.fromAppLink("cehoproxy://import?url=https://example.com"))
        assertNull(ShareLink.fromAppLink("cehoproxy://pair?u=https%3A%2F%2Fevil.example%2F"))
    }

    @Test fun readsTokenAndFormField() {
        assertEquals(token, ShareLink.tokenOf("/p/$token"))
        assertNull(ShareLink.tokenOf("/p/$token/extra"))
        assertEquals("https://example.com/sub?a=1&b=2", ShareLink.formField("x=1&link=https%3A%2F%2Fexample.com%2Fsub%3Fa%3D1%26b%3D2", "link"))
        assertNull(ShareLink.formField("link=+", "link"))
    }
}
