package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class SubscriptionQrTest {
    @Test fun matrixIsSquareWithQuietZoneAndFinderPattern() {
        val m = SubscriptionQr.modules("https://cebceh.com/sub/abc")
        assertEquals(m.size, m.first().size)
        assertFalse("quiet zone corner is light", m[0][0])
        assertTrue("finder pattern starts after quiet zone", m[4][4])
        assertFalse("finder ring is light", m[5][5])
    }

    @Test fun differentLinksGiveDifferentCodes() {
        val a = SubscriptionQr.modules("https://cebceh.com/sub/abc")
        val b = SubscriptionQr.modules("https://cebceh.com/sub/xyz")
        assertFalse(a.contentDeepEquals(b))
    }
}
