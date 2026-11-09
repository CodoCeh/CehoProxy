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

    @Test fun appFailingOnSeveralSitesWhileOthersWorkIsOffered() {
        val policy = AppFailurePolicy()
        policy.record("other.app", "ok.example", true, 1_000)
        assertNull(policy.record("bank.app", "api.bank.ru", false, 2_000))
        assertNull(policy.record("bank.app", "api.bank.ru", false, 3_000))
        assertNull(policy.record("bank.app", "cdn.bank.ru", false, 4_000))
        assertNull(policy.record("bank.app", "api.bank.ru", false, 5_000))
        val offer = policy.record("bank.app", "auth.other.ru", false, 6_000)
        assertEquals("bank.app", offer?.packageName)
        assertEquals(setOf("bank.ru", "other.ru"), offer?.sites)
    }

    @Test fun appWithOneBrokenSiteIsLeftToTheSitePolicy() {
        val policy = AppFailurePolicy()
        policy.record("other.app", "ok.example", true, 1_000)
        repeat(8) { assertNull(policy.record("browser", "blocked.com", false, 2_000L + it)) }
    }

    @Test fun appThatSometimesWorksOrWithNoWorkingNeighboursIsNotOffered() {
        val policy = AppFailurePolicy()
        repeat(6) { assertNull(policy.record("a", if (it % 2 == 0) "x.ru" else "y.ru", false, 1_000L + it)) }
        val mixed = AppFailurePolicy()
        mixed.record("b", "ok.example", true, 0)
        mixed.record("a", "x.ru", false, 1_000); mixed.record("a", "y.ru", false, 1_100)
        mixed.record("a", "x.ru", true, 1_200)
        assertNull(mixed.record("a", "y.ru", false, 1_300))
    }

    @Test fun appExceptionAppliesOnlyInExcludeMode() {
        assertTrue(SiteHintPolicy.appApplies(true, listOf("x"), "bank.app"))
        assertFalse(SiteHintPolicy.appApplies(false, emptyList(), "bank.app"))
        assertFalse(SiteHintPolicy.appApplies(true, listOf("bank.app"), "bank.app"))
    }

    @Test fun directFailuresAreTheirOwnKind() {
        assertTrue(SiteHintPolicy.failedDirect("direct", 0, 500))
        assertFalse(SiteHintPolicy.failedDirect("vless", 0, 500))
        assertFalse(SiteHintPolicy.failedDirect("direct", 20, 500))
    }

    @Test fun tunnelOfferNeedsServersAndAnUnroutedSite() {
        assertTrue(SiteHintPolicy.tunnelApplies(false, listOf("yandex.ru"), emptySet(), true, "chatgpt.com"))
        assertTrue(SiteHintPolicy.tunnelApplies(false, listOf("chatgpt.com"), emptySet(), true, "chatgpt.com"))
        assertFalse(SiteHintPolicy.tunnelApplies(false, emptyList(), emptySet(), false, "chatgpt.com"))
        assertFalse(SiteHintPolicy.tunnelApplies(false, emptyList(), setOf("chatgpt.com"), true, "chatgpt.com"))
        assertFalse(SiteHintPolicy.tunnelApplies(true, listOf("chatgpt.com"), emptySet(), true, "api.chatgpt.com"))
        assertTrue(SiteHintPolicy.tunnelApplies(true, emptyList(), emptySet(), true, "chatgpt.com"))
    }

    @Test fun everyKindHasItsOwnWordingAndHintKey() {
        val keys = BypassOffer.Kind.entries.map { BypassOffer.hintKey(it, "x.ru") }
        assertEquals(3, keys.toSet().size)
        assertTrue(BypassOffer.text(BypassOffer.Kind.TUNNEL, "chatgpt.com").confirm.contains("через VPN"))
        assertTrue(BypassOffer.text(BypassOffer.Kind.APP, "Банк").title.contains("«Банк»"))
    }
}
