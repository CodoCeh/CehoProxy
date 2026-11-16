package ru.cebers.cehoproxy

import android.content.Context
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.NodeShareParser
import ru.cebers.cehoproxy.core.ProfileCodec

@RunWith(AndroidJUnit4::class)
class SitesInputUiTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    private fun prefs(name: String) = rule.activity.getSharedPreferences(name, Context.MODE_PRIVATE)

    @After fun cleanup() { prefs("profile").edit().remove("data").commit() }

    @Test fun rfDomainsCopiedFromBrowserAreAddedToDirectList() {
        val node = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443#Sites").single()
        prefs("profile").edit().putString("data", ProfileCodec.encode(ClientProfile(nodes = listOf(node)))).commit()
        prefs("onboarding").edit().putBoolean("completed", true).commit()
        rule.activityRule.scenario.recreate()
        rule.onNodeWithTag("header-settings").performClick()
        rule.onNodeWithTag("settings-routing").performClick()
        rule.onNodeWithTag("route-tab:1").performScrollTo().performClick()
        rule.onNodeWithTag("sites-open").performScrollTo().performClick()
        rule.onNodeWithTag("sites-input").performTextInput("https://xn--80az8a.xn--d1aqf.xn--p1ai/, https://%D0%B4%D0%BE%D0%BC.%D1%80%D1%84/, мвд.рф?x=1")
        rule.onNodeWithTag("sites-add").performClick()
        rule.onNodeWithTag("site-item:наш.дом.рф").assertIsDisplayed()
        rule.onNodeWithTag("site-item:дом.рф").assertIsDisplayed()
        rule.onNodeWithTag("site-item:мвд.рф").assertIsDisplayed()
        rule.onNodeWithTag("sites-done").performClick()
        rule.waitForIdle()
        val saved = ProfileCodec.decode(prefs("profile").getString("data", "{}")!!)
        assertEquals(listOf("наш.дом.рф", "дом.рф", "мвд.рф"), saved.sites)
    }
}
