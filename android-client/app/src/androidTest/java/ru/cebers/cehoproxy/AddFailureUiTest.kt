package ru.cebers.cehoproxy

import android.content.Context
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.After
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.ProfileCodec
import ru.cebers.cehoproxy.core.ProxyNode
import ru.cebers.cehoproxy.core.SubscriptionMetadata
import androidx.compose.ui.test.performScrollTo

@RunWith(AndroidJUnit4::class)
class AddFailureUiTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    private fun prefs(name: String) = rule.activity.getSharedPreferences(name, Context.MODE_PRIVATE)

    @Before fun setup() {
        prefs("profile").edit().putString("data", ProfileCodec.encode(ClientProfile(apps = listOf("a.b")))).commit()
        prefs("onboarding").edit().putBoolean("completed", true).commit()
        rule.activityRule.scenario.recreate()
    }

    @After fun cleanup() { prefs("profile").edit().remove("data").commit() }

    @Test fun failedSubscriptionRefreshIsAnnouncedOnTheSubscriptionScreenAndKeepsServers() {
        val url = "https://no-such-host.invalid/sub"
        val metadata = SubscriptionMetadata(title = "Недоступная", sourceHost = "no-such-host.invalid", sourceUrl = url)
        val node = ProxyNode("Old", "socks", "localhost", 1080, "socks://localhost:1080#Old", subscriptionMetadata = metadata)
        prefs("profile").edit().putString("data", ProfileCodec.encode(ClientProfile(nodes = listOf(node), subscriptionUrls = listOf(url), apps = listOf("a.b")))).commit()
        rule.activityRule.scenario.recreate()
        rule.onNodeWithTag("home-server").performClick()
        rule.onNodeWithTag("subscription-manage:0").performScrollTo().performClick()
        rule.onNodeWithText("Обновить сейчас").performScrollTo().performClick()
        rule.waitUntil(30_000) { rule.onAllNodesWithTag("result-banner").fetchSemanticsNodes().isNotEmpty() }
        rule.onNodeWithText("Подписка не ответила", substring = true).assertIsDisplayed()
        rule.onNodeWithTag("result-banner-close").performClick()
        rule.waitUntil(5_000) { rule.onAllNodesWithTag("result-banner").fetchSemanticsNodes().isEmpty() }
        assertTrue(ProfileCodec.decode(prefs("profile").getString("data", "{}")!!).nodes.size == 1)
    }

    @Test fun addingTheSameServerTwiceReportsDuplicateInsteadOfSilence() {
        val link = "socks5://dup.example:1080#Dup"
        repeat(2) {
            rule.onNodeWithTag("header-add-subscription").performClick()
            rule.onNodeWithTag("import-input").performTextInput(link)
            rule.onNodeWithTag("import-submit").performClick()
            rule.waitUntil(15_000) { rule.onAllNodesWithTag("import-input").fetchSemanticsNodes().isEmpty() }
        }
        rule.waitUntil(5_000) { rule.onAllNodesWithTag("result-banner").fetchSemanticsNodes().isNotEmpty() }
        rule.onNodeWithTag("result-banner").assertIsDisplayed()
        rule.onNodeWithText("Добавлено узлов: 0 · дубликатов: 1", substring = true).assertIsDisplayed()
    }
}
