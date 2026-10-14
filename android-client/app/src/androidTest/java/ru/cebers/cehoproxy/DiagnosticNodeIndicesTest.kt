package ru.cebers.cehoproxy

import org.junit.Assert.*
import org.junit.Test

class DiagnosticNodeIndicesTest {
    @Test fun keepsDefaultAndRequestedComparisonOrder() {
        assertEquals(listOf(0, 1, 2, 4), DiagnosticNodeIndices.parse(null, 10))
        assertEquals(listOf(8, 0), DiagnosticNodeIndices.parse("8,0", 10))
        assertEquals(listOf(500), DiagnosticNodeIndices.parse("500", 501))
    }

    @Test fun rejectsMalformedDuplicateAndOutOfBoundsIndices() {
        for (argument in listOf("", "-1", "8,,0", "8,node", "0,0", "501", "999999")) {
            try {
                DiagnosticNodeIndices.parse(argument, 501)
                fail("Malformed diagnostic indices must fail")
            } catch (_: IllegalArgumentException) { }
        }
        try {
            DiagnosticNodeIndices.parse("8,0", 8)
            fail("Snapshot bounds must be checked")
        } catch (_: IllegalArgumentException) { }
    }
}
