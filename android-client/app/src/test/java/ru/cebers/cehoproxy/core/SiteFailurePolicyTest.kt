package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SiteFailurePolicyTest {
    @Test fun registrableNameKeepsTheSiteNotTheSubdomain() {
        assertEquals("example.com", SiteFailurePolicy.registrable("cdn.a.Example.com."))
        assertEquals("bbc.co.uk", SiteFailurePolicy.registrable("www.bbc.co.uk"))
        assertNull(SiteFailurePolicy.registrable("192.168.1.5"))
        assertNull(SiteFailurePolicy.registrable("printer.local"))
        assertNull(SiteFailurePolicy.registrable("localhost"))
    }

    @Test fun threeFailuresWhileOtherSitesWorkOfferTheSite() {
        val policy = SiteFailurePolicy()
        assertNull(policy.record("ok.example", true, 1_000))
        assertNull(policy.record("a.blocked.com", false, 2_000))
        assertNull(policy.record("b.blocked.com", false, 3_000))
        assertEquals("blocked.com", policy.record("blocked.com", false, 4_000))
        assertNull(policy.record("blocked.com", false, 5_000))
    }

    @Test fun nothingIsOfferedWhenNoSiteWorksAtAll() {
        val policy = SiteFailurePolicy()
        repeat(5) { assertNull(policy.record("blocked.com", false, 1_000L + it)) }
    }

    @Test fun oldFailuresAndSuccessfulLoadsDoNotAdd() {
        val policy = SiteFailurePolicy()
        policy.record("ok.example", true, 0)
        policy.record("slow.com", false, 1_000)
        policy.record("slow.com", false, 2_000)
        assertNull(policy.record("slow.com", false, 200_000))
        policy.record("ok.example", true, 200_500)
        policy.record("mixed.com", false, 201_000)
        policy.record("mixed.com", false, 201_500)
        policy.record("mixed.com", true, 202_000)
        assertNull(policy.record("mixed.com", false, 203_000))
    }

    @Test fun offerOnlyWhenExceptionsMakeSenseAndNotYetListed() {
        assertTrue(SiteHintPolicy.applies(false, listOf("yandex.ru"), "blocked.com"))
        assertFalse(SiteHintPolicy.applies(true, emptyList(), "blocked.com"))
        assertFalse(SiteHintPolicy.applies(false, listOf("blocked.com"), "blocked.com"))
        assertFalse(SiteHintPolicy.applies(false, listOf("blocked.com"), "cdn.blocked.com"))
    }

    @Test fun snoozeAndGapAreRespected() {
        assertTrue(SiteHintPolicy.due(0, 0, SiteHintPolicy.MIN_GAP_MILLIS))
        assertFalse(SiteHintPolicy.due(0, 1_000, 2_000))
        assertFalse(SiteHintPolicy.due(SiteHintPolicy.NEVER, 0, 10_000_000))
        assertFalse(SiteHintPolicy.due(5_000_000, 0, 4_000_000))
    }

    @Test fun onlySilentClosedConnectionsThroughTheTunnelCountAsFailures() {
        assertTrue(SiteHintPolicy.failed("vless", 0, 500))
        assertFalse(SiteHintPolicy.failed("vless", 10, 500))
        assertFalse(SiteHintPolicy.failed("direct", 0, 500))
        assertFalse(SiteHintPolicy.failed("vless", 0, 0))
    }
}
