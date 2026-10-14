package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class SiteListInputTest {
    @Test fun splitsCommaSpaceAndNewlineSeparatedSites() {
        val parsed = SiteListInput.parse("vk.com, https://Ya.ru/search\ndocs.example;  vk.com")
        assertEquals(listOf("vk.com", "ya.ru", "docs.example"), parsed.hosts)
        assertEquals(emptyList<String>(), parsed.rejected)
    }

    @Test fun keepsCyrillicDomainsAndRejectsNonDomains() {
        val parsed = SiteListInput.parse("госуслуги.рф, abc, 1.2.3.4x, -bad.ru")
        assertEquals(listOf("госуслуги.рф"), parsed.hosts)
        assertEquals(listOf("abc", "1.2.3.4x", "-bad.ru"), parsed.rejected)
    }

    @Test fun emptyInputGivesNothing() {
        val parsed = SiteListInput.parse("  , ,\n")
        assertEquals(emptyList<String>(), parsed.hosts)
        assertEquals(emptyList<String>(), parsed.rejected)
    }
}
