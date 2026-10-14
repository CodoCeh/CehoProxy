package ru.cebers.cehoproxy

import android.content.ClipData
import android.content.Context
import android.content.Intent
import android.app.Notification
import android.app.NotificationManager
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import android.os.Build
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.assertTextEquals
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import io.nekohasekai.libbox.Libbox
import ru.cebers.cehoproxy.core.NodeShareParser
import ru.cebers.cehoproxy.core.RoutePolicy
import ru.cebers.cehoproxy.core.RoutingConfig
import ru.cebers.cehoproxy.core.ClientProfile
import ru.cebers.cehoproxy.core.ProxyNode
import ru.cebers.cehoproxy.core.ProfileCodec
import ru.cebers.cehoproxy.core.SubscriptionMetadata
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.After
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class SubscriptionImportUiTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    @Before fun clearSavedProfile() {
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().remove("data").commit()
        rule.activity.getSharedPreferences("onboarding", Context.MODE_PRIVATE).edit().putBoolean("completed", true).commit()
        rule.activity.getSharedPreferences("appearance", Context.MODE_PRIVATE).edit().remove("theme").commit()
        rule.activityRule.scenario.recreate()
    }

    @After fun removeSavedProfile() {
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().remove("data").commit()
        rule.activity.getSharedPreferences("onboarding", Context.MODE_PRIVATE).edit().putBoolean("completed", true).commit()
        rule.activity.getSharedPreferences("appearance", Context.MODE_PRIVATE).edit().remove("theme").commit()
    }

    private fun goHome() {
        while (rule.onAllNodesWithTag("page-back").fetchSemanticsNodes().isNotEmpty()) rule.onNodeWithTag("page-back").performClick()
    }

    private fun openSettings() {
        goHome()
        rule.onNodeWithTag("header-settings").performClick()
    }

    private fun openServers() {
        openSettings()
        rule.onNodeWithTag("settings-latency").performClick()
    }

    private fun openSubscriptions() {
        openSettings()
        rule.onNodeWithTag("settings-subscriptions").performClick()
    }

    private fun openRouting() {
        openSettings()
        rule.onNodeWithTag("settings-routing").performClick()
    }

    private fun openImportDialog() {
        goHome()
        rule.onNodeWithTag("header-add-subscription").performClick()
        rule.onNodeWithTag("add-sheet-paste").performClick()
    }

    @Test fun dpiProxyModeTogglePreservesSelectedAppsAndManualSites() {
        val old = ClientProfile(apps = listOf("video.app"), localDpiApps = listOf("video.app"), sites = listOf("manual.example"))
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(old)).commit()
        rule.activityRule.scenario.recreate()
        openRouting()
        rule.onNodeWithText("Дополнительно").performScrollTo().performClick()
        rule.onNodeWithTag("dpi-disable:video.app").performScrollTo().assertIsDisplayed().performClick()
        val saved = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!)
        assertEquals(emptyList<String>(), saved.localDpiApps)
        assertEquals(old.apps, saved.apps)
        assertEquals(old.sites, saved.sites)
    }

    @Test fun nodesShowUnmeasuredMetricsWithoutOfferingOfflineTrafficTests() {
        val node = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443#UI%20test").single()
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(ClientProfile(nodes = listOf(node)))).commit()
        rule.activityRule.scenario.recreate()
        openServers()
        rule.onNodeWithText("VLESS · Ещё не измерено", useUnmergedTree = true).performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("node-check:0", useUnmergedTree = true).assertDoesNotExist()
        openSettings()
        rule.onNodeWithTag("settings-update-section").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("settings-update-check").performScrollTo().assertIsDisplayed()
    }

    @Test fun countryChosenInSettingsAfterTwoStepOnboardingPersistsWithoutChangingAppsOrSites() {
        val node = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443#Onboarding").single()
        val profile = ClientProfile(nodes = listOf(node), apps = listOf("com.android.chrome"), sites = listOf("manual.example"))
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(profile)).commit()
        rule.activity.getSharedPreferences("onboarding", Context.MODE_PRIVATE).edit().putBoolean("completed", false).commit()
        rule.activityRule.scenario.recreate()
        rule.onNodeWithTag("onboarding-next").performScrollTo().performClick()
        rule.onNodeWithTag("onboarding-finish").performScrollTo().performClick()
        openRouting()
        rule.onNodeWithTag("route-tab:2").performClick()
        rule.onNodeWithTag("country-selector").performScrollTo().performClick()
        rule.onNodeWithTag("country-option:ru").performClick()
        rule.onNodeWithTag("country-coverage").performScrollTo().assertTextEquals("Домены и IP-адреса. Наборы правил сохранены в приложении и доступны без сети. Ручные правила сайтов важнее.")
        rule.onNodeWithTag("country-selector").performScrollTo().assertTextEquals("Россия")
        var saved = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!)
        assertEquals("ru", saved.directCountryCode)
        assertEquals(profile.apps, saved.apps)
        assertEquals(profile.sites, saved.sites)
        assertEquals(false, saved.excludeSelectedApps)
        rule.activityRule.scenario.recreate()
        openRouting()
        rule.onNodeWithTag("route-tab:2").performClick()
        rule.onNodeWithTag("country-selector").performScrollTo().assertTextEquals("Россия").performClick()
        rule.onNodeWithTag("country-option:none").performClick()
        saved = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!)
        assertEquals("", saved.directCountryCode)
        assertEquals(profile.sites, saved.sites)
    }

    @Test fun importedFullConfigDisablesCountrySelectionWithoutDeletingExistingChoice() {
        val config = """{"inbounds":[{"type":"tun"}],"outbounds":[{"type":"direct","tag":"direct"}]}"""
        val profile = ClientProfile(importedConfig = config, directCountryCode = "de")
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(profile)).commit()
        rule.activityRule.scenario.recreate()
        openRouting()
        rule.onNodeWithTag("route-tab:2").performClick()
        rule.onNodeWithTag("country-selector").assertIsNotEnabled()
        rule.onNodeWithTag("country-coverage").assertTextEquals("Правила страны CehoProxy не применяются к импортированной конфигурации. Её маршруты сохраняются.")
        val saved = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}")!!)
        assertEquals("de", saved.directCountryCode)
        assertEquals(config, saved.importedConfig)
    }

    @Test fun selectedThemeIsPersistedAcrossActivityRestart() {
        openSettings()
        rule.onNodeWithTag("theme-light").performScrollTo().performClick()
        rule.onNodeWithTag("theme-light").assertIsSelected()
        assertEquals("light", rule.activity.getSharedPreferences("appearance", Context.MODE_PRIVATE).getString("theme", null))

        rule.activityRule.scenario.recreate()
        openSettings()
        rule.onNodeWithTag("theme-light").assertIsSelected()
        rule.onNodeWithTag("theme-dark").performScrollTo().performClick()
        assertEquals("dark", rule.activity.getSharedPreferences("appearance", Context.MODE_PRIVATE).getString("theme", null))
    }

    @Test fun lightThemeRendersHomeRulesAndImportDialog() {
        openSettings()
        rule.onNodeWithTag("theme-light").performScrollTo().performClick()
        rule.onNodeWithTag("theme-light").assertIsSelected()
        rule.onNodeWithText("ИНТЕРФЕЙС").assertIsDisplayed()
        openRouting()
        rule.onNodeWithTag("route-tab:1").performClick()
        rule.onNodeWithText("Маршрутизация").assertIsDisplayed()
        rule.onNodeWithText("Сайты напрямую").performScrollTo().assertIsDisplayed()
        goHome()
        openImportDialog()
        rule.onNodeWithText("Вставьте ссылку или выберите способ добавления").assertIsDisplayed()
        rule.onNodeWithTag("import-input").assertIsDisplayed()
    }

    @Test fun firstRunGuidesUserThroughSetupAndCanBeCompletedWithoutSubscription() {
        rule.activity.getSharedPreferences("onboarding", Context.MODE_PRIVATE).edit().putBoolean("completed", false).commit()
        rule.activityRule.scenario.recreate()

        rule.onNodeWithText("Добавьте ссылку").assertIsDisplayed()
        rule.onNodeWithTag("onboarding-next").assertIsNotEnabled()
        rule.onNodeWithText("Настроить позже").performScrollTo().performClick()

        assertEquals(true, rule.activity.getSharedPreferences("onboarding", Context.MODE_PRIVATE).getBoolean("completed", false))
        rule.activityRule.scenario.recreate()
        rule.onNodeWithTag("home-vortex").assertIsDisplayed()
        rule.onNodeWithText("VPN выключен").assertIsDisplayed()
    }

    @Test fun clipboardButtonImportsCopiedShareLink() {
        rule.activity.runOnUiThread {
            val clipboard = rule.activity.getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
            clipboard.setPrimaryClip(ClipData.newPlainText("CehoProxy test", "socks5://user:pass@clipboard.example:1080#clipboard-smoke"))
        }
        rule.waitForIdle()
        if (rule.onAllNodesWithText("Позже").fetchSemanticsNodes().isNotEmpty()) rule.onNodeWithText("Позже").performClick()
        openImportDialog()
        rule.onNodeWithText("Из буфера").performScrollTo().performClick()
        rule.waitUntil(20_000) {
            val saved = org.json.JSONObject(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}") ?: "{}")
            saved.optJSONArray("nodes")?.length() == 1
        }
        val saved = org.json.JSONObject(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}") ?: "{}")
        val nodes = saved.getJSONArray("nodes")
        assertEquals("clipboard-smoke", nodes.getJSONObject(nodes.length() - 1).getString("name"))
    }

    @Test fun helpPageOffersSubscriptionBot() {
        openSettings()
        rule.onNodeWithText("Помощь и поддержка").performScrollTo().assertIsDisplayed().performClick()
        rule.onNodeWithText("Открыть @CebCehVPN_bot").performScrollTo().assertIsDisplayed()
    }

    @Test fun homeScreenUsesVortexAsConnectControlAndOffersInlineNodeAdd() {
        rule.onNodeWithText("СЕРВЕРЫ").assertIsDisplayed()
        rule.onNodeWithTag("home-add-node").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("home-vortex").performScrollTo().assertIsDisplayed()
        rule.onAllNodesWithText("Подключить", substring = true).assertCountEquals(0)
        rule.onNodeWithTag("header-add-subscription").assertIsDisplayed()
    }

    @Test fun homeNodePickerChangesSelectedNode() {
        val nodes = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@one.example:443#Node-A") +
            NodeShareParser.parse("vless://22222222-2222-2222-2222-222222222222@two.example:443#Node-B")
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit()
            .putString("data", ProfileCodec.encode(ClientProfile(nodes = nodes))).commit()
        rule.activityRule.scenario.recreate()
        rule.onNodeWithTag("home-node-option:0").performClick()
        rule.onNodeWithTag("home-node-option:1").performScrollTo().assertIsDisplayed().performClick()
        val saved = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}") ?: "{}")
        assertEquals(1, saved.selected)
    }

    @Test fun failedImportKeepsInputForCorrection() {
        openImportDialog()
        rule.onNodeWithTag("import-input").performTextInput("unsupported://example")
        rule.onNodeWithTag("import-submit").performClick()
        rule.onNodeWithText("Ссылка не содержит поддерживаемый узел").assertIsDisplayed()
        rule.onNodeWithTag("import-input").assertTextEquals("Ссылка или конфигурация", "unsupported://example")
        rule.onNodeWithText("Отмена").performClick()
        rule.onNodeWithTag("import-input").assertDoesNotExist()
    }

    @Test fun successfulImportClosesFormAndShowsSelectedNode() {
        openImportDialog()
        rule.onNodeWithTag("import-input").performTextInput("socks5://user:pass@demo.example:1080#Demo-node")
        rule.onNodeWithTag("import-submit").performClick()
        rule.waitUntil(10_000) {
            ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}") ?: "{}").nodes.size == 1
        }
        rule.onNodeWithTag("import-input").assertDoesNotExist()
        rule.onNodeWithText("Demo-node").assertIsDisplayed()
    }

    @Test fun liveProviderSubscriptionImportsThroughTheAppWhenUrlProvided() {
        val url = androidx.test.platform.app.InstrumentationRegistry.getArguments().getString("liveSubscriptionUrl")
        org.junit.Assume.assumeTrue("Pass a user-owned URL only for an explicit live acceptance run", !url.isNullOrBlank())
        openImportDialog()
        rule.onNodeWithTag("import-input").performTextInput(url!!)
        rule.onNodeWithTag("import-submit").performClick()
        rule.waitUntil(90_000) {
            ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}") ?: "{}").nodes.isNotEmpty()
        }
        val nodes = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}") ?: "{}") .nodes
        org.junit.Assert.assertTrue("The live subscription returned no usable nodes", nodes.isNotEmpty())
        val types = nodes.map { it.type }.distinct().sorted()
        android.util.Log.i("CehoLiveSubscription", "Imported ${nodes.size} live nodes; protocol types: ${types.joinToString()}")
    }

    @Test fun captureConfiguredHomeForVisualReview() {
        val arguments = androidx.test.platform.app.InstrumentationRegistry.getArguments()
        if (arguments.getString("captureUi") != "true") return
        val nodes = NodeShareParser.parse("socks5://demo.example:1080#Demo%20%C2%B7%20Main") + NodeShareParser.parse("socks5://backup.example:1080#Demo%20%C2%B7%20Backup")
        val profile = ClientProfile(nodes = nodes, apps = listOf("com.android.chrome", "com.android.settings"))
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(profile)).commit()
        rule.activityRule.scenario.recreate()
        rule.waitForIdle()
        val instrumentation = androidx.test.platform.app.InstrumentationRegistry.getInstrumentation()
        val directory = rule.activity.getExternalFilesDir(null)!!
        fun capture(name: String) {
            rule.mainClock.advanceTimeBy(1_000)
            Thread.sleep(600)
            rule.waitForIdle()
            val bitmap = instrumentation.uiAutomation.takeScreenshot()
            java.io.File(directory, "$name.png").outputStream().use { bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it) }
            bitmap.recycle()
        }
        capture("home")
        openSubscriptions()
        capture("subscriptions")
        goHome()
        rule.onNodeWithTag("header-add-subscription").performClick()
        capture("add")
        rule.onNodeWithTag("add-sheet-paste").performClick()
        capture("import")
        rule.onNodeWithText("Отмена").performClick()
        openRouting()
        capture("apps")
        rule.onNodeWithTag("route-tab:2").performClick()
        capture("rules")
        openSettings()
        capture("settings")
    }

    @Test fun settingsExposeOptInExpiryReminders() {
        openSettings()
        rule.onNodeWithText("Напоминать о сроке").performScrollTo().assertIsDisplayed()
    }

    @Test fun siteListLabelExplainsBothDirections() {
        openRouting()
        rule.onNodeWithTag("route-tab:1").performClick()
        rule.onNodeWithText("Сайты напрямую").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("sites-mode").performScrollTo().performClick()
        rule.onNodeWithText("Сайты через VPN").performScrollTo().assertIsDisplayed()
        val saved = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}") ?: "{}")
        assertEquals(true, saved.onlySites)
    }

    @Test fun settingsKeepTransferVisibleAndExpandSystemHelpOnDemand() {
        openSettings()
        rule.onNodeWithText("ДАННЫЕ").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("Открыть настройки Android").assertDoesNotExist()
        rule.onNodeWithText("Постоянный VPN").performClick()
        rule.onNodeWithText("Открыть настройки Android").performScrollTo().assertIsDisplayed()
    }

    @Test fun restartOnboardingActionRemainsInsideScrollableSettingsContent() {
        openSettings()
        rule.onNodeWithTag("restart-onboarding").performScrollTo().assertIsDisplayed()
    }

    @Test fun exportMenuOffersSettingsSubscriptionsAndFullBackup() {
        openSettings()
        rule.onNodeWithText("Экспорт").performScrollTo().performClick()
        rule.onNodeWithText("Выберите, что перенести. Файлы могут содержать ссылки подписок или собственную конфигурацию с данными доступа.").assertIsDisplayed()
        rule.onNodeWithText("Только настройки").assertIsDisplayed()
        rule.onNodeWithText("Только подписки").assertIsDisplayed()
        rule.onNodeWithText("Всё вместе").assertIsDisplayed()
        rule.onNodeWithText("Выберите, что перенести. Файлы могут содержать ссылки подписок или собственную конфигурацию с данными доступа.").assertIsDisplayed()
    }

    @Test fun enablingExpiryRemindersIsPersisted() {
        grantNotificationPermissionForTest()
        val expiry = System.currentTimeMillis() / 1000 + 5 * 86400
        val raw = """{"type":"vless","tag":"Reminder node","server":"node.example","server_port":443,"uuid":"synthetic-id"}"""
        val node = ProxyNode("Reminder node", "vless", "node.example", 443, raw, expiry, SubscriptionMetadata(expiresAtEpochSeconds = expiry))
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(ClientProfile(nodes = listOf(node)))).commit()
        rule.activityRule.scenario.recreate()
        openSettings()
        rule.onNodeWithContentDescription("Напоминать о сроке").performScrollTo().assertIsDisplayed().performClick()
        rule.waitUntil(5_000) {
            val enabled = org.json.JSONObject(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}") ?: "{}").optBoolean("expiryRemindersEnabled")
            val alarmIds = rule.activity.getSharedPreferences("expiry_reminders", Context.MODE_PRIVATE).getStringSet("scheduled_ids", emptySet()).orEmpty()
            enabled && alarmIds.size == 3
        }
        rule.activity.let { activity ->
            ExpiryReminderScheduler.BootReceiver().onReceive(activity, Intent(Intent.ACTION_BOOT_COMPLETED))
        }
        rule.waitUntil(5_000) {
            rule.activity.getSharedPreferences("expiry_reminders", Context.MODE_PRIVATE).getStringSet("scheduled_ids", emptySet()).orEmpty().size == 3
        }
    }

    @Test fun reminderReceiverPostsNotification() {
        grantNotificationPermissionForTest()
        val manager = rule.activity.getSystemService(NotificationManager::class.java)
        assertEquals(true, manager.areNotificationsEnabled())
        val expiry = System.currentTimeMillis() / 1000 + 86400
        ExpiryReminderScheduler.ReminderReceiver().onReceive(rule.activity, Intent().putExtra("expiry", expiry))
        rule.waitUntil(3_000) {
            manager.activeNotifications.any { it.notification.extras.getString(Notification.EXTRA_TITLE) == "Скоро закончится подписка" }
        }
        assertEquals(true, manager.activeNotifications.any { it.notification.extras.getString(Notification.EXTRA_TITLE) == "Скоро закончится подписка" })
        manager.cancelAll()
    }

    @Test fun subscriptionMetadataAndRenewActionAreVisible() {
        val metadata = SubscriptionMetadata(1_900_000_000L, 1024, 2048, 8192, "План пользователя", "Информация провайдера", "Объявление", "https://t.me/provider_support", null, null, null, "https://t.me/provider_renew", true)
        val raw = """{"type":"vless","tag":"Тестовый узел","server":"node.example","server_port":443,"uuid":"synthetic-id"}"""
        val profile = ClientProfile(nodes = listOf(ProxyNode("Тестовый узел", "vless", "node.example", 443, raw, metadata.expiresAtEpochSeconds, metadata)))
        rule.activity.runOnUiThread {
            rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(profile)).commit()
        }
        rule.activityRule.scenario.recreate()
        rule.waitForIdle()
        openServers()
        rule.onNodeWithTag("subscription-details-toggle:0").performScrollTo().performClick()
        rule.onAllNodesWithText("План пользователя").assertCountEquals(2)
        rule.onAllNodesWithText("Трафик:", substring = true).assertCountEquals(1)
        rule.onNodeWithText("Информация провайдера").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("Уведомления от провайдера: включены").performScrollTo().assertIsDisplayed()
        rule.onNodeWithText("Продлить подписку").performScrollTo().assertIsDisplayed()
    }

    @Test fun perAppAndPerSiteRoutesAreSelectableAndAcceptedBySingBox() {
        val first = NodeShareParser.parse("vless://11111111-1111-1111-1111-111111111111@edge.example:443#Node-A").single()
        val second = NodeShareParser.parse("vless://22222222-2222-2222-2222-222222222222@backup.example:443#Node-B").single()
        val profile = ClientProfile(nodes = listOf(first, second), apps = listOf("com.example.chat"), sites = listOf("chat.example"))
        rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).edit().putString("data", ProfileCodec.encode(profile)).commit()
        rule.activityRule.scenario.recreate()
        openRouting()
        rule.onNodeWithText("Дополнительно").performClick()
        rule.onNodeWithText("ВЫХОД ДЛЯ ПРИЛОЖЕНИЙ").assertIsDisplayed()
        rule.onNodeWithTag("route-tab:1").performClick()
        rule.onNodeWithText("ВЫХОД ДЛЯ САЙТОВ").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("route-select:chat.example").performScrollTo().performClick()
        rule.onNodeWithText("Node-B").performClick()
        rule.onNodeWithTag("route-select:chat.example").assertTextEquals("Node-B")
        rule.onNodeWithTag("route-tab:2").performClick()
        rule.onNodeWithText("Автоматически выбирать узел").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("routing-save").performScrollTo().performClick()
        rule.onNodeWithText("Правила сохранены").performScrollTo().assertIsDisplayed()
        val saved = ProfileCodec.decode(rule.activity.getSharedPreferences("profile", Context.MODE_PRIVATE).getString("data", "{}") ?: "{}")
        assertEquals("sites=${saved.sites}, routeKeys=${saved.siteNodeRoutes.keys}", second.routeId, saved.siteNodeRoutes["chat.example"])
        val config = RoutingConfig.build(saved.nodes, saved.selected, RoutePolicy(
            apps = saved.apps,
            sites = saved.sites,
            appNodeRoutes = saved.appNodeRoutes,
            siteNodeRoutes = saved.siteNodeRoutes,
            autoRotationEnabled = true,
            autoRotationIntervalSeconds = 900,
        ))
        Libbox.checkConfig(config)
    }

    @Test fun singBox114AcceptsConvertedDesktopOutbounds() {
        val inputs = listOf(
            """[{"remarks":"Reality gRPC profile","outbounds":[{"tag":"proxy","protocol":"vless","settings":{"vnext":[{"address":"edge.example","port":443,"users":[{"id":"11111111-1111-1111-1111-111111111111","flow":""}]}]},"streamSettings":{"network":"grpc","security":"reality","realitySettings":{"serverName":"front.example","fingerprint":"chrome","publicKey":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","shortId":"abcd"},"grpcSettings":{"serviceName":"service"}}}]}]""",
            """{"outbounds":[{"tag":"vless-ws","protocol":"vless","settings":{"vnext":[{"address":"vless.example","port":443,"users":[{"id":"synthetic-id","flow":""}]}]},"streamSettings":{"network":"ws","security":"tls","tlsSettings":{"serverName":"front.example"},"wsSettings":{"path":"/tunnel","headers":{"Host":"cdn.example"}}}}]}""",
            """proxies:
  - name: clash-trojan
    type: trojan
    server: trojan.example
    port: 443
    password: synthetic-pass
    sni: front.example
""",
            """{"servers":[{"remarks":"legacy-ss","server":"ss.example","server_port":8388,"method":"aes-128-gcm","password":"synthetic-pass"}]}""",
            "socks5://user:pass@socks.example:1080#socks",
            """vless://synthetic-id@vless.example:443#happ-vless
                vmess://eyJwcyI6IlN5bnRoZXRpYyBWTWVzcyIsImFkZCI6InZtZXNzLmV4YW1wbGUiLCJwb3J0IjoiNDQzIiwiaWQiOiJzeW50aGV0aWMtaWQifQ==#happ-vmess
                socks://user:pass@socks.example:1080#happ-socks
                trojan://synthetic-pass@trojan.example:443#happ-trojan
                ss://YWVzLTEyOC1nY206c3ludGhldGljLXBhc3N3b3JkQHNzLmV4YW1wbGU6ODM4OA==#happ-ss
                hysteria2://synthetic-pass@hy2.example:443?sni=front.example#happ-hy2""".trimIndent(),
        )
        val nodes = inputs.flatMap(NodeShareParser::parse)
        assertEquals(11, nodes.size)
        nodes.forEach { Libbox.checkConfig(RoutingConfig.build(it, RoutePolicy())) }
    }

    private fun grantNotificationPermissionForTest() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().uiAutomation
                .grantRuntimePermission("ru.cebers.cehoproxy", android.Manifest.permission.POST_NOTIFICATIONS)
        }
    }
}
