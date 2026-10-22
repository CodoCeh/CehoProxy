package ru.cebers.cehoproxy

import android.content.Context
import androidx.compose.ui.test.assertIsOff
import androidx.compose.ui.test.assertIsOn
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import ru.cebers.cehoproxy.core.*

class AutoSwitchUiTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    private fun node(name: String, host: String) = ProxyNode(name, "socks", "localhost", 1080, "socks://localhost:1080#$name", subscriptionMetadata = SubscriptionMetadata(sourceHost = host))

    private fun stored() = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!)

    @Before fun setup() {
        val profile = ClientProfile(nodes = listOf(node("one", "first.example"), node("two", "second.example")), autoRotationEnabled = true, excludeSelectedApps = true)
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(profile)).commit()
        rule.activity.getSharedPreferences("onboarding", Context.MODE_PRIVATE).edit().putBoolean("completed", true).commit()
        rule.activityRule.scenario.recreate()
        rule.onNodeWithTag("header-settings").performClick()
        rule.onNodeWithTag("settings-latency").performClick()
    }

    @After fun cleanup() {
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().remove("data").commit()
    }

    @Test fun globalSwitchTurnsAutomaticSwitchingOffAndHidesSubscriptionSwitches() {
        rule.onNodeWithTag("servers-auto-switch", useUnmergedTree = true).assertIsOn().performClick()
        rule.onNodeWithTag("servers-auto-switch", useUnmergedTree = true).assertIsOff()
        assertEquals(false, stored().autoRotationEnabled)
        assertTrue(rule.onAllNodesWithTag("subscription-auto:0", useUnmergedTree = true).fetchSemanticsNodes().isEmpty())
    }

    @Test fun subscriptionSwitchKeepsOnlyThatSubscriptionOutOfAutoPool() {
        rule.onNodeWithTag("subscription-auto:0", useUnmergedTree = true).performScrollTo().assertIsOn().performClick()
        rule.onNodeWithTag("subscription-auto:0", useUnmergedTree = true).assertIsOff()
        assertEquals(listOf("first.example"), stored().autoDisabledSubscriptions)
        assertEquals(true, stored().autoRotationEnabled)
        rule.onNodeWithTag("subscription-auto:0", useUnmergedTree = true).performClick()
        assertTrue(stored().autoDisabledSubscriptions.isEmpty())
    }
}
