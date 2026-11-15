package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class UsageLedgerTest {
    @Test fun countsPerAppAndSubscriptionAndRollsOver() {
        var ledger = UsageLedger()
        ledger = ledger.add("a", "sub.example", Usage(10, 100), "2026-11-15")
        ledger = ledger.add("a", null, Usage(1, 2), "2026-11-15")
        assertEquals(Usage(11, 102), ledger.appsToday["a"])
        assertEquals(Usage(10, 100), ledger.subscriptionsMonth["sub.example"])
        ledger = ledger.add("b", "sub.example", Usage(0, 5), "2026-11-16")
        assertTrue("a" !in ledger.appsToday)
        assertEquals(Usage(11, 102), ledger.appsMonth["a"])
        assertEquals(Usage(10, 105), ledger.subscriptionsMonth["sub.example"])
        ledger = ledger.add("b", "sub.example", Usage(0, 1), "2026-12-01")
        assertEquals(mapOf("b" to Usage(0, 1)), ledger.appsMonth)
        assertEquals(mapOf("sub.example" to Usage(0, 1)), ledger.subscriptionsMonth)
    }

    @Test fun survivesEncoding() {
        val ledger = UsageLedger().add("a", "s", Usage(3, 4), "2026-11-15")
        assertEquals(ledger, UsageLedger.decode(ledger.encode()))
        assertEquals(UsageLedger(), UsageLedger.decode("not json"))
    }

    @Test fun quotaWarnsAtEightyAndNinetyFive() {
        assertEquals(0, QuotaPolicy.level(79, 100))
        assertEquals(80, QuotaPolicy.level(80, 100))
        assertEquals(95, QuotaPolicy.level(99, 100))
        assertEquals(0, QuotaPolicy.level(500, 0))
        assertEquals(0, QuotaPolicy.level(null, 100))
        assertEquals("Подписка «QA»: израсходовано 85% трафика (870,4 МБ из 1,0 ГБ)", QuotaPolicy.message("QA", 912_680_550, 1_073_741_824))
    }
}
