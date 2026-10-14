package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class YoutubeAdPolicyTest {
    private val node = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443#Test").single()

    private fun rejectRules(config: String): List<JSONObject> {
        val rules = JSONObject(config).getJSONObject("route").getJSONArray("rules")
        return (0 until rules.length()).map { rules.getJSONObject(it) }.filter { it.optString("action") == "reject" && it.has("domain_suffix") }
    }

    @Test fun enabledRejectsAdDomains() {
        val rule = rejectRules(RoutingConfig.build(node, RoutePolicy(blockYoutubeAds = true))).single()
        val suffixes = rule.getJSONArray("domain_suffix")
        assertTrue((0 until suffixes.length()).map { suffixes.getString(it) }.contains("doubleclick.net"))
    }

    @Test fun disabledAddsNoRejectRule() {
        assertFalse(rejectRules(RoutingConfig.build(node, RoutePolicy())).isNotEmpty())
    }

    @Test fun flagSurvivesProfileCodec() {
        assertTrue(ProfileCodec.decode(ProfileCodec.encode(ClientProfile(blockYoutubeAds = true))).blockYoutubeAds)
    }

    @Test fun enabledRoutesYoutubeDirectWithoutSelectedApps() {
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy(blockYoutubeAds = true)))
        val rules = config.getJSONObject("route").getJSONArray("rules")
        val direct = (0 until rules.length()).map { rules.getJSONObject(it) }.filter { it.optJSONArray("package_name")?.getString(0) == YoutubeAdPolicy.PACKAGE && it.optString("outbound") == "direct" }
        assertTrue(direct.any { it.optBoolean("tls_record_fragment") })
    }

    @Test fun disabledKeepsYoutubeOnNode() {
        val config = JSONObject(RoutingConfig.build(node, RoutePolicy()))
        assertFalse(config.toString().contains(YoutubeAdPolicy.PACKAGE))
    }
}
