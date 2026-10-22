package ru.cebers.cehoproxy

import android.content.Context
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.assertTextEquals
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.assertCountEquals
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.ProfileCodec
import ru.cebers.cehoproxy.core.ProxyNode
import ru.cebers.cehoproxy.core.SubscriptionMetadata

private const val GIB = 1024L * 1024 * 1024

@RunWith(AndroidJUnit4::class)
class QuietModeUiTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    private val now get() = System.currentTimeMillis() / 1000

    private fun prefs(name: String) = rule.activity.getSharedPreferences(name, Context.MODE_PRIVATE)
    private fun stored() = ProfileCodec.decode(prefs("profile").getString("data", "{}")!!)
    private fun pressBack() { androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation.performGlobalAction(android.accessibilityservice.AccessibilityService.GLOBAL_ACTION_BACK); rule.waitForIdle() }

    private fun subscriptionNode(name: String, url: String, title: String, expiresAt: Long? = null, used: Long? = null, total: Long? = null): ProxyNode {
        val metadata = SubscriptionMetadata(expiresAtEpochSeconds = expiresAt, downloadBytes = used, totalBytes = total, title = title, sourceHost = java.net.URL(url).host, sourceUrl = url)
        return ProxyNode(name, "socks", "localhost", 1080, "socks://localhost:1080#$name", expiresAt, metadata)
    }

    private fun install(profile: ClientProfile) {
        prefs("profile").edit().putString("data", ProfileCodec.encode(profile)).commit()
        prefs("onboarding").edit().putBoolean("completed", true).commit()
        prefs("appearance").edit().remove("theme").commit()
        rule.activityRule.scenario.recreate()
    }

    @Before fun resetProfile() { prefs("profile").edit().remove("data").commit() }
    @After fun cleanup() { prefs("profile").edit().remove("data").commit(); prefs("onboarding").edit().putBoolean("completed", true).commit() }

    @Test fun homeSummaryShowsRealTermAndTrafficShareOfTheSelectedSubscription() {
        val url = "https://one.example/sub"
        val node = subscriptionNode("Only", url, "Личная подписка", expiresAt = now + 18 * 86_400L + 3_600, used = 28 * GIB, total = 100 * GIB)
        install(ClientProfile(nodes = listOf(node), subscriptionUrls = listOf(url), apps = listOf("a.b")))
        rule.onNodeWithTag("home-term").assertTextContains("18 дней", substring = true)
        rule.onNodeWithText("Личная подписка").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("home-traffic").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("использовано", substring = true).assertIsDisplayed()
        rule.onNodeWithText("Осталось", substring = true).assertIsDisplayed()
    }

    @Test fun homeSummaryDoesNotInventLimitOrTerm() {
        val url = "https://two.example/sub"
        val node = subscriptionNode("Only", url, "Без лимита в ответе", used = 5 * GIB)
        install(ClientProfile(nodes = listOf(node), subscriptionUrls = listOf(url), apps = listOf("a.b")))
        rule.onNodeWithTag("home-term").assertTextContains("Не указан", substring = true)
        rule.onNodeWithText("Лимит не указан").performScrollTo().assertIsDisplayed()
    }

    @Test fun expiredSubscriptionIsMarkedAsExpired() {
        val url = "https://old.example/sub"
        install(ClientProfile(nodes = listOf(subscriptionNode("Only", url, "Старая", expiresAt = now - 100)), subscriptionUrls = listOf(url), apps = listOf("a.b")))
        rule.onNodeWithTag("home-term").assertTextContains("Истекла", substring = true)
    }

    @Test fun summaryFollowsTheSubscriptionOfTheSelectedServer() {
        val firstUrl = "https://first.example/sub"
        val secondUrl = "https://second.example/sub"
        val nodes = listOf(
            subscriptionNode("Node-1", firstUrl, "Первая", expiresAt = now + 10 * 86_400L + 3_600),
            subscriptionNode("Node-2", secondUrl, "Вторая", expiresAt = now + 40 * 86_400L + 3_600),
        )
        install(ClientProfile(nodes = nodes, subscriptionUrls = listOf(firstUrl, secondUrl), apps = listOf("a.b")))
        rule.onNodeWithText("Первая").assertIsDisplayed()
        rule.onNodeWithTag("home-term").assertTextContains("10 дней", substring = true)
        rule.onNodeWithTag("home-server").performClick()
        rule.onNodeWithTag("home-node-option:1").performScrollTo().performClick()
        assertEquals(1, stored().selected)
        rule.onNodeWithTag("page-back").performClick()
        rule.onNodeWithText("Вторая").assertIsDisplayed()
        rule.onNodeWithTag("home-term").assertTextContains("40 дней", substring = true)
    }

    @Test fun routeModeIsAnExplicitChoiceBetweenSelectedAndAllExceptSelected() {
        install(ClientProfile(apps = listOf("a.b"), excludeSelectedApps = false))
        rule.onNodeWithTag("home-rules").performScrollTo().performClick()
        rule.onNodeWithTag("route-mode:only").assertIsSelected()
        rule.onNodeWithTag("route-mode:all").performClick()
        rule.onNodeWithTag("route-mode:all").assertIsSelected()
        assertEquals(true, stored().excludeSelectedApps)
        rule.onNodeWithTag("route-mode:only").performClick()
        assertEquals(false, stored().excludeSelectedApps)
    }

    @Test fun addSheetNeedsInputAndSecondSubscriptionKeepsCountryAndOnboarding() {
        val first = subscriptionNode("First", "https://first.example/sub", "Первая")
        install(ClientProfile(nodes = listOf(first), apps = listOf("a.b"), directCountryCode = "de"))
        rule.onNodeWithTag("header-add-subscription").performClick()
        rule.onNodeWithTag("import-submit").assertIsNotEnabled()
        rule.onNodeWithTag("import-input").performTextInput("socks5://second.example:1080#Second")
        rule.onNodeWithTag("import-submit").assertIsEnabled().performClick()
        rule.waitUntil(15_000) { stored().nodes.size == 2 }
        rule.waitUntil(5_000) { rule.onAllNodesWithTag("import-input").fetchSemanticsNodes().isEmpty() }
        assertEquals("de", stored().directCountryCode)
        assertEquals(listOf("a.b"), stored().apps)
        assertEquals(true, prefs("onboarding").getBoolean("completed", false))
        rule.onNodeWithTag("home-vortex").assertIsDisplayed()
    }

    @Test fun vortexIsTheOnlyConnectControl() {
        install(ClientProfile(nodes = listOf(subscriptionNode("Only", "https://one.example/sub", "Подписка")), apps = listOf("a.b")))
        rule.onNodeWithContentDescription("VPN выключен. Нажмите, чтобы подключить.").assertIsDisplayed()
        rule.onAllNodesWithText("Подключить").assertCountEquals(0)
        rule.onAllNodesWithText("Отключить").assertCountEquals(0)
        rule.onNodeWithTag("home-status").assertTextEquals("Не подключено")
    }

    @Test fun systemBackInsideOnboardingReturnsToThePreviousStep() {
        prefs("onboarding").edit().putBoolean("completed", false).commit()
        rule.activityRule.scenario.recreate()
        rule.onNodeWithTag("onboarding-next").performClick()
        rule.onNodeWithText("Добавьте подписку").assertIsDisplayed()
        pressBack()
        rule.waitUntil(5_000) { rule.onAllNodesWithText("Настроить подключение").fetchSemanticsNodes().isNotEmpty() }
        rule.onNodeWithText("Настроить подключение").assertIsDisplayed()
    }
}
