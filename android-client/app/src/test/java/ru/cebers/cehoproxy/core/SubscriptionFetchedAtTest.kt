package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.ZoneId

class SubscriptionFetchedAtTest {
    private val utc = ZoneId.of("UTC")
    private val now = 1_760_356_800_000L

    @Test fun sameDayShowsTimeOnly() {
        val at = now - 3 * 60 * 60 * 1000L
        assertEquals("Обновлено 09:00", SubscriptionRefreshPolicy.fetchedLabel(at, now, utc))
    }

    @Test fun otherDayShowsDateAndTime() {
        val at = now - 24 * 60 * 60 * 1000L - 60 * 60 * 1000L
        assertEquals("Обновлено 12.10 11:00", SubscriptionRefreshPolicy.fetchedLabel(at, now, utc))
    }

    @Test fun refreshStampsEveryFetchedUrl() {
        val url = "https://a.example/sub"
        val profile = ClientProfile(subscriptionUrls = listOf(url))
        val fetched = mapOf(url to listOf(ProxyNode("n", "vless", "s", 443, "raw", subscriptionMetadata = SubscriptionMetadata(sourceHost = "a.example"))))
        val result = SubscriptionRefreshPolicy.applyFetchedSources(profile, fetched, now)
        assertEquals(now, result.subscriptionFetchedAtByUrl[url])
    }

    @Test fun perUrlTimestampSurvivesProfileRoundTrip() {
        val profile = ClientProfile(subscriptionUrls = listOf("https://a.example/sub"), subscriptionFetchedAtByUrl = mapOf("https://a.example/sub" to now))
        assertEquals(now, ProfileCodec.decode(ProfileCodec.encode(profile)).subscriptionFetchedAtByUrl["https://a.example/sub"])
    }
}
