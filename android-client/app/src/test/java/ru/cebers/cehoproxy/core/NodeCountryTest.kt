package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class NodeCountryTest {
    @Test fun flagEmojiWinsOverNameAndUnknownNamesStayNull() {
        assertEquals("RU", NodeCountry.resolve("🇷🇺 Россия-1"))
        assertEquals("DE", NodeCountry.resolve("[TOP 04 • 94.6 Mbps] 🇩🇪 Германия-4 · Резервный"))
        assertEquals("NL", NodeCountry.resolve("Нидерланды-14"))
        assertEquals("US", NodeCountry.resolve("united states edge"))
        assertNull(NodeCountry.resolve("↔️ Автовыбор"))
    }

    @Test fun displayNameIsRussian() {
        assertEquals("Россия", NodeCountry.displayName("RU"))
    }

    @Test fun flagEmojiInNodeNameGivesCountryCode() {
        assertEquals("RU", NodeCountry.flagCode("[TOP 01 • 95.8 Mbps] 🇷🇺 Россия-1"))
        assertEquals(null, NodeCountry.flagCode("Без флага"))
    }

    @Test fun countryWordWithoutFlagGivesCode() {
        assertEquals("SE", NodeCountry.resolve("Sweden 2 Premium"))
        assertEquals("DE", NodeCountry.resolve("Германия-3"))
    }
}
