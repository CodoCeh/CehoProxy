package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AutoDisabledSubscriptionTest {
    private fun node(name: String, uuid: String, host: String): ProxyNode =
        NodeShareParser.parse("vless://$uuid@$uuid.example:443#$name").single().copy(subscriptionMetadata = SubscriptionMetadata(sourceHost = host))

    private val first = node("Финляндия-1", "11111111-1111-1111-1111-111111111111", "first.example")
    private val second = node("Германия-1", "22222222-2222-2222-2222-222222222222", "second.example")

    private fun autoPool(policy: RoutePolicy): List<String> {
        val outbounds = JSONObject(RoutingConfig.build(listOf(first, second), 0, policy)).getJSONArray("outbounds")
        val selector = (0 until outbounds.length()).map { outbounds.getJSONObject(it) }.first { it.optString("tag") == OrderedFailoverConfig.GLOBAL_TAG }
        val tags = selector.getJSONArray("outbounds")
        return (0 until tags.length()).map(tags::getString).filter { it != "block" }
    }

    @Test fun subscriptionWithAutoSwitchOffStaysOutOfTheAutoPool() {
        val pool = autoPool(RoutePolicy(autoRotationEnabled = true, autoDisabledSubscriptions = setOf("first.example")))
        assertEquals(listOf("proxy-1"), pool)
    }

    @Test fun autoPoolFallsBackToAllNodesWhenEverySubscriptionIsOff() {
        val pool = autoPool(RoutePolicy(autoRotationEnabled = true, autoDisabledSubscriptions = setOf("first.example", "second.example")))
        assertEquals(listOf("proxy-0", "proxy-1"), pool)
    }

    @Test fun autoDisabledSubscriptionsSurviveProfileRoundTrip() {
        val profile = ProfileCodec.decode(ProfileCodec.encode(ClientProfile(autoDisabledSubscriptions = listOf("first.example"))))
        assertEquals(listOf("first.example"), profile.autoDisabledSubscriptions)
        assertTrue(ProfileCodec.decode(ProfileCodec.encode(ClientProfile())).autoDisabledSubscriptions.isEmpty())
    }
}
