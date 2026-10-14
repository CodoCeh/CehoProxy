package ru.cebers.cehoproxy.vpn

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class AppScopePolicyTest {
    @Test fun selectedModeRequiresAtLeastOneInstalledPackage() {
        assertEquals(
            "Выберите хотя бы одно приложение для туннеля. Остальные приложения останутся напрямую",
            AppScopePolicy.validate(emptyList(), emptyList()),
        )
    }

    @Test fun exclusionModeAllowsAllAppsWhenNoExceptionsAreSelected() {
        assertNull(AppScopePolicy.validate(emptyList(), emptyList(), excludeSelectedApps = true))
    }

    @Test fun exclusionModeRequiresConfiguredExceptionsToMatchSavedSelection() {
        assertNull(AppScopePolicy.validate(listOf("com.example.direct"), listOf("com.example.direct"), true))
        assertEquals(
            "Не удалось применить выбранные приложения к VPN. Проверьте, что приложения установлены, и выберите их снова",
            AppScopePolicy.validate(listOf("com.example.direct"), emptyList(), true),
        )
    }
}
