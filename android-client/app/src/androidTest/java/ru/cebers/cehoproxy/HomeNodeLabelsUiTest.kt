package ru.cebers.cehoproxy

import android.content.Context
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.performTextClearance
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.assertTextEquals
import androidx.compose.ui.test.assertIsOn
import androidx.compose.ui.test.assertIsOff
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import ru.cebers.cehoproxy.core.*

class HomeNodeLabelsUiTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    @Before fun setup() {
        val nodes = listOf("one", "two").map { name -> ProxyNode(name, "socks", "localhost", 1080, "socks://localhost:1080#$name", subscriptionMetadata = SubscriptionMetadata(title = if (name == "one") "Partner subscription" else "Other provider")) }
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(ClientProfile(nodes = nodes, apps = listOf("test.app"), sites = listOf("test.example")))).commit()
        rule.activity.getSharedPreferences("onboarding", Context.MODE_PRIVATE).edit().putBoolean("completed", true).commit()
        rule.activityRule.scenario.recreate()
    }

    private fun openSettings() = rule.onNodeWithTag("header-settings").performClick()

    private fun openServers() {
        openSettings()
        rule.onNodeWithTag("settings-latency").performClick()
    }

    private fun openRouting() {
        openSettings()
        rule.onNodeWithTag("settings-routing").performClick()
    }

    private fun back() = rule.onNodeWithTag("page-back").performClick()

    @After fun cleanup() {
        rule.activity.getSharedPreferences("verified_node_countries", Context.MODE_PRIVATE).edit().clear().commit()
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().remove("data").commit()
    }

    @Test fun homeAutoChoicePersistsWithoutChangingRoutesAndTitleIsVisible() {
        openServers()
        rule.onNodeWithText("Partner subscription", useUnmergedTree = true).performScrollTo().assertIsDisplayed()
        back()
        back()
        openRouting()
        rule.onNodeWithTag("route-tab:2").performClick()
        rule.onNodeWithText("Дополнительно").performScrollTo().performClick()
        rule.onNodeWithTag("rules-auto-node-switch", useUnmergedTree = true).performScrollTo().assertIsOff().performClick()
        rule.onNodeWithTag("rules-auto-node-switch", useUnmergedTree = true).assertIsOn()
        rule.onNodeWithTag("routing-save").performScrollTo().performClick()
        val saved = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!)
        assertEquals(listOf("test.app"), saved.apps)
        assertEquals(listOf("test.example"), saved.sites)
        assertEquals(true, saved.autoRotationEnabled)
        rule.activityRule.scenario.recreate()
        openRouting()
        rule.onNodeWithTag("route-tab:2").performClick()
        rule.onNodeWithText("Дополнительно").performScrollTo().performClick()
        rule.onNodeWithTag("rules-auto-node-switch", useUnmergedTree = true).performScrollTo().assertIsOn().performClick()
        rule.onNodeWithTag("routing-save").performScrollTo().performClick()
        assertEquals(false, ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!).autoRotationEnabled)
    }

    @Test fun homePrimaryActionsRemainAccessible() {
        rule.onNodeWithTag("home-vortex").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("home-node-option:0").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("header-add-subscription").assertIsDisplayed()
        rule.onNodeWithTag("header-settings").performClick()
        rule.onNodeWithTag("settings-routing").assertIsDisplayed()
        back()
        rule.onNodeWithTag("home-vortex").assertIsDisplayed()
    }

    @Test fun subscriptionSearchUsesSourceAndKeepsOriginalSelectionIndex() {
        openServers()
        rule.onNodeWithTag("subscriptions-search").performTextInput("Other provider")
        rule.onNodeWithTag("home-node-option:0").assertDoesNotExist()
        rule.onNodeWithTag("home-node-option:1").performScrollTo().performClick()
        val saved = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!)
        assertEquals(1, saved.selected)
        assertEquals(listOf("one", "two"), saved.nodes.map { it.name })
        assertEquals(listOf("test.app"), saved.apps)
        assertEquals(listOf("test.example"), saved.sites)
        rule.onNodeWithTag("subscriptions-search").performScrollTo().performTextClearance()
        rule.onNodeWithTag("subscriptions-search").performTextInput("one")
        rule.onNodeWithTag("home-node-option:0").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("home-node-option:1").assertDoesNotExist()
        rule.onNodeWithText("Partner subscription", useUnmergedTree = true).performScrollTo().assertIsDisplayed()
    }


    @Test fun sameTitleWithDifferentMetadataKeepsBothDetailsAccessible() {
        val current = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!)
        val nodes = current.nodes.mapIndexed { index, node ->
            node.copy(subscriptionMetadata = SubscriptionMetadata(title = "Same provider", infoText = "Details $index", totalBytes = (index + 1) * 1024L))
        }
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(current.copy(nodes = nodes))).commit()
        rule.activityRule.scenario.recreate()
        openServers()
        rule.onNodeWithTag("subscription-details-toggle:0").performScrollTo().performClick()
        rule.onNodeWithText("Details 0").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("Details 1").assertDoesNotExist()
        rule.onNodeWithTag("subscription-details-toggle:1").performScrollTo().performClick()
        rule.onNodeWithText("Details 1").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("Details 0").assertDoesNotExist()
        rule.onNodeWithTag("home-node-option:1").performScrollTo().performClick()
        assertEquals(1, ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!).selected)
    }


    @Test fun notificationOnlySubscriptionMetadataIsVisible() {
        val current = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!)
        val node = current.nodes.first().copy(subscriptionMetadata = SubscriptionMetadata(notifyOnExpiry = true))
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(current.copy(nodes = listOf(node)))).commit()
        rule.activityRule.scenario.recreate()
        openServers()
        rule.onNodeWithTag("subscription-details-toggle:0").performScrollTo().performClick()
        rule.onNodeWithText("Уведомления от провайдера: включены").performScrollTo().assertIsDisplayed()
    }

}
