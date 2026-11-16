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

    @Test fun acceptsRfDomainsCopiedFromBrowser() {
        val parsed = SiteListInput.parse(
            "https://xn--80az8a.xn--d1aqf.xn--p1ai/, https://%D0%B4%D0%BE%D0%BC.%D1%80%D1%84/, наш.дом.рф?x=1, мвд.рф#top, xn--80aswg.xn--p1ai"
        )
        assertEquals(listOf("наш.дом.рф", "дом.рф", "мвд.рф", "сайт.рф"), parsed.hosts)
        assertEquals(emptyList<String>(), parsed.rejected)
        assertEquals("xn--80az8a.xn--d1aqf.xn--p1ai", RoutingConfig.ruleDomain(parsed.hosts.first()))
    }

    @Test fun emptyInputGivesNothing() {
        val parsed = SiteListInput.parse("  , ,\n")
        assertEquals(emptyList<String>(), parsed.hosts)
        assertEquals(emptyList<String>(), parsed.rejected)
    }
}
