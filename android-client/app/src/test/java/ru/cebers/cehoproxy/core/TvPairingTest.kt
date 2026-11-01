package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class TvPairingTest {
    private val token = "0123456789abcdef0123456789abcdef"
    private val url = TvPairing.url("192.168.1.20", 41234, token)

    @Test fun recognisesOnlyPairingAddresses() {
        assertTrue(TvPairing.isPairingUrl(url))
        assertFalse(TvPairing.isPairingUrl("https://192.168.1.20:41234/p/$token"))
        assertFalse(TvPairing.isPairingUrl("http://192.168.1.20:41234/p/short"))
        assertFalse(TvPairing.isPairingUrl("http://example.com/sub/abc"))
        assertFalse(TvPairing.isPairingUrl("$url?x=1"))
    }

    @Test fun appLinkRoundTrips() {
        assertEquals(url, TvPairing.fromAppLink(TvPairing.appLink(url)))
        assertNull(TvPairing.fromAppLink("cehoproxy://import?url=https://example.com"))
        assertNull(TvPairing.fromAppLink("cehoproxy://pair?u=https%3A%2F%2Fevil.example%2F"))
    }

    @Test fun readsTokenAndFormField() {
        assertEquals(token, TvPairing.tokenOf("/p/$token"))
        assertNull(TvPairing.tokenOf("/p/$token/extra"))
        assertEquals("https://example.com/sub?a=1&b=2", TvPairing.formField("x=1&link=https%3A%2F%2Fexample.com%2Fsub%3Fa%3D1%26b%3D2", "link"))
        assertNull(TvPairing.formField("link=+", "link"))
    }
}
