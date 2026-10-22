package ru.cebers.cehoproxy

import android.content.Context
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class NewFeaturesUiTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    @Before fun setup() {
        rule.activity.getSharedPreferences("onboarding", Context.MODE_PRIVATE).edit().putBoolean("completed", true).commit()
        rule.activityRule.scenario.recreate()
    }

    @Test fun connectionDiagnosticsShowsFourChecks() {
        rule.onNodeWithTag("header-settings").performClick()
        rule.onNodeWithTag("settings-help").performScrollTo().performClick()
        rule.onNodeWithTag("settings-diagnostics").performScrollTo().performClick()
        rule.waitUntil(15_000) { rule.onAllNodesWithText("Интернет без VPN").fetchSemanticsNodes().isNotEmpty() }
        rule.onNodeWithText("Сеть телефона").assertIsDisplayed()
        rule.onNodeWithText("Туннель CehoProxy").assertIsDisplayed()
        rule.onNodeWithText("Интернет через VPN").assertIsDisplayed()
        rule.onNodeWithText("Закрыть").performClick()
    }
}
