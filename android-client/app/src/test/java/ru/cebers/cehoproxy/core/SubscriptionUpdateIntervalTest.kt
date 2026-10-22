package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SubscriptionUpdateIntervalTest {
    private val hour = SubscriptionRefreshPolicy.HOUR_MILLIS
    private val now = 1_000_000_000_000L
    private fun node(host: String, interval: Int?) = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@$host.example:443#n")
        .single().copy(subscriptionMetadata = SubscriptionMetadata(sourceHost = "$host.example", updateIntervalHours = interval))
    private val fast = "https://fast.example/sub"
    private val slow = "https://slow.example/sub"
    private fun profile(refresh: Int = 4, fastFetched: Long = now - 2 * hour, slowFetched: Long = now - 2 * hour) = ClientProfile(
        nodes = listOf(node("fast", 1), node("slow", null)), subscriptionUrls = listOf(fast, slow), subscriptionRefreshHours = refresh,
        subscriptionFetchedAtByUrl = mapOf(fast to fastFetched, slow to slowFetched),
    )

    @Test fun headerValueIsReadInHours() {
        assertEquals(6, SubscriptionMetadata.fromValues(mapOf("profile-update-interval" to "6")).updateIntervalHours)
        assertEquals(2, SubscriptionMetadata.fromValues(mapOf("Profile-Update-Interval" to "1.5")).updateIntervalHours)
    }

    @Test fun nonsenseValuesAreIgnoredAndHugeOnesCapped() {
        assertNull(SubscriptionMetadata.fromValues(mapOf("profile-update-interval" to "abc")).updateIntervalHours)
        assertNull(SubscriptionMetadata.fromValues(mapOf("profile-update-interval" to "0")).updateIntervalHours)
        assertNull(SubscriptionMetadata.fromValues(mapOf("profile-update-interval" to "-3")).updateIntervalHours)
        assertEquals(SubscriptionMetadata.MAX_UPDATE_INTERVAL_HOURS, SubscriptionMetadata.fromValues(mapOf("profile-update-interval" to "99999")).updateIntervalHours)
    }

    @Test fun intervalSurvivesProfileRoundTrip() {
        val decoded = ProfileCodec.decode(ProfileCodec.encode(profile()))
        assertEquals(1, SubscriptionRefreshPolicy.providerHours(decoded, fast))
        assertNull(SubscriptionRefreshPolicy.providerHours(decoded, slow))
    }

    @Test fun providerIntervalBeatsTheAppDefaultPerSubscription() {
        val p = profile()
        assertEquals(1, SubscriptionRefreshPolicy.intervalHoursFor(p, fast))
        assertEquals(4, SubscriptionRefreshPolicy.intervalHoursFor(p, slow))
        assertEquals(1, SubscriptionRefreshPolicy.backgroundHours(p))
    }

    @Test fun refreshOffInSettingsWinsOverTheProvider() {
        val p = profile(refresh = 0)
        assertEquals(0, SubscriptionRefreshPolicy.intervalHoursFor(p, fast))
        assertEquals(0, SubscriptionRefreshPolicy.backgroundHours(p))
        assertEquals(emptyList<String>(), SubscriptionRefreshPolicy.dueUrls(p, now))
    }

    @Test fun onlyTheSubscriptionWhoseIntervalPassedIsDue() {
        assertEquals(listOf(fast), SubscriptionRefreshPolicy.dueUrls(profile(), now))
        assertEquals(listOf(fast, slow), SubscriptionRefreshPolicy.dueUrls(profile(slowFetched = now - 5 * hour), now))
    }

    @Test fun aRunSlightlyBeforeTheIntervalStillCounts() {
        assertEquals(listOf(fast), SubscriptionRefreshPolicy.dueUrls(profile(fastFetched = now - hour + 5 * 60 * 1000L), now))
    }
}
