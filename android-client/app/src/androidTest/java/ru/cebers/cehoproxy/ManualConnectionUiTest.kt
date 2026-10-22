package ru.cebers.cehoproxy

import android.content.Context
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import androidx.test.ext.junit.runners.AndroidJUnit4
import io.nekohasekai.libbox.Libbox
import org.json.JSONObject
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.ManualConnection
import ru.cebers.cehoproxy.core.ManualConnectionPolicy
import ru.cebers.cehoproxy.core.ManualProtocol
import ru.cebers.cehoproxy.core.NodeShareParser
import ru.cebers.cehoproxy.core.ProfileCodec
import ru.cebers.cehoproxy.core.RoutePolicy
import ru.cebers.cehoproxy.core.RoutingConfig

@RunWith(AndroidJUnit4::class)
class ManualConnectionUiTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    private fun prefs(name: String) = rule.activity.getSharedPreferences(name, Context.MODE_PRIVATE)
    private fun stored() = ProfileCodec.decode(prefs("profile").getString("data", "{}")!!)

    private fun install(profile: ClientProfile) {
        prefs("profile").edit().putString("data", ProfileCodec.encode(profile)).commit()
        prefs("onboarding").edit().putBoolean("completed", true).commit()
        rule.activityRule.scenario.recreate()
    }

    private fun assumeTouchDevice() = org.junit.Assume.assumeFalse(
        "On Android TV a focused text field keeps the window redrawing and the Compose test never goes idle",
        rule.activity.packageManager.hasSystemFeature(android.content.pm.PackageManager.FEATURE_LEANBACK),
    )

    private fun openManualForm() {
        rule.onNodeWithTag("header-add-subscription").performClick()
        rule.onNodeWithTag("add-sheet-manual").performScrollTo().performClick()
        rule.onNodeWithTag("manual-host").assertIsDisplayed()
    }

    @Before fun reset() { prefs("profile").edit().remove("data").commit() }
    @After fun cleanup() { prefs("profile").edit().remove("data").commit() }

    @Test fun formAddsSocksServerWithSpecialCharacterPasswordAndEngineAcceptsIt() {
        assumeTouchDevice()
        install(ClientProfile(apps = listOf("a.b")))
        openManualForm()
        rule.onNodeWithTag("manual-protocol:SOCKS5").performClick()
        rule.onNodeWithTag("manual-name").performTextInput("Офис")
        rule.onNodeWithTag("manual-host").performTextInput("10.20.30.40")
        rule.onNodeWithTag("manual-port").performTextInput("1081")
        rule.onNodeWithTag("manual-user").performTextInput("dom\\user@corp")
        rule.onNodeWithTag("manual-password").performTextInput("p@ss:w0rd/#?%+ я")
        rule.onNodeWithTag("manual-submit").performScrollTo().performClick()
        rule.waitUntil(15_000) { stored().nodes.size == 1 }
        val node = stored().nodes.single()
        assertEquals("Офис", node.name)
        assertEquals("10.20.30.40", node.server)
        assertEquals(1081, node.port)
        val outbound = outboundFor(node.server, RoutingConfig.build(node, RoutePolicy(apps = listOf("a.b"))))
        assertEquals("socks", outbound.getString("type"))
        assertEquals("dom\\user@corp", outbound.getString("username"))
        assertEquals("p@ss:w0rd/#?%+ я", outbound.getString("password"))
        rule.waitUntil(5_000) { rule.onAllNodesWithTag("manual-host").fetchSemanticsNodes().isEmpty() }
        rule.onNodeWithTag("home-vortex").assertIsDisplayed()
    }

    @Test fun httpsProxyWithoutLoginShowsTheReasonAndAddsNothing() {
        assumeTouchDevice()
        install(ClientProfile(apps = listOf("a.b")))
        openManualForm()
        rule.onNodeWithTag("manual-host").performTextInput("proxy.example")
        rule.waitUntil(5_000) { rule.onAllNodes(androidx.compose.ui.test.hasTestTag("manual-submit") and androidx.compose.ui.test.isEnabled()).fetchSemanticsNodes().isNotEmpty() }
        rule.onNodeWithTag("manual-submit").performScrollTo().performClick()
        rule.waitUntil(5_000) { rule.onAllNodesWithTag("manual-error").fetchSemanticsNodes().isNotEmpty() }
        rule.onNodeWithTag("manual-error").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("Для HTTPS-прокси нужны логин и пароль").assertIsDisplayed()
        assertTrue(stored().nodes.isEmpty())
    }

    @Test fun allThreeProtocolsProduceConfigsTheEngineAcceptsWithExactCredentials() {
        val password = "S3cr3t:/@#?&=+% ü"
        listOf(
            ManualConnection(ManualProtocol.HTTPS, host = "naive.example", port = "443", username = "user name", password = password, serverName = "front.example"),
            ManualConnection(ManualProtocol.SOCKS5, host = "socks.example", username = "user name", password = password),
            ManualConnection(ManualProtocol.HTTP, host = "http.example", port = "3128", username = "user name", password = password),
            ManualConnection(ManualProtocol.SOCKS5, host = "2001:db8::10", port = "1080"),
        ).forEach { form ->
            assertEquals(null, ManualConnectionPolicy.validate(form))
            val node = NodeShareParser.parse(ManualConnectionPolicy.toUri(form)).single()
            val config = RoutingConfig.build(node, RoutePolicy())
            Libbox.checkConfig(config)
            val outbound = outboundFor(node.server, config)
            if (form.username.isNotEmpty()) {
                assertEquals(form.username, outbound.getString("username"))
                assertEquals(password, outbound.getString("password"))
            }
            if (form.protocol == ManualProtocol.HTTPS) {
                val tls = outbound.getJSONObject("tls")
                assertEquals("front.example", tls.getString("server_name"))
            }
        }
    }

    @Test fun manuallyAddedServerCanBeRemovedAfterConfirmation() {
        val node = NodeShareParser.parse(ManualConnectionPolicy.toUri(ManualConnection(ManualProtocol.SOCKS5, host = "10.0.0.9", name = "Временный"))).single()
        install(ClientProfile(nodes = listOf(node), apps = listOf("a.b")))
        rule.onNodeWithTag("home-server").performClick()
        rule.onNodeWithTag("node-remove:0").performScrollTo().performClick()
        rule.onNodeWithText("Удалить сервер?").assertIsDisplayed()
        assertEquals(1, stored().nodes.size)
        rule.onNodeWithTag("node-remove-confirm").performClick()
        rule.waitUntil(5_000) { stored().nodes.isEmpty() }
    }

    private fun outboundFor(server: String, config: String): JSONObject {
        val outbounds = JSONObject(config).getJSONArray("outbounds")
        return (0 until outbounds.length()).map { outbounds.getJSONObject(it) }.first { it.optString("server") == server }
    }
}
