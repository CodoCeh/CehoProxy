package ru.cebers.cehoproxy

internal enum class ThemePreference(val storageValue: String, val label: String) {
    SYSTEM("system", "Системная"),
    LIGHT("light", "Светлая"),
    DARK("dark", "Тёмная");

    companion object {
        fun fromStorage(value: String?): ThemePreference = entries.firstOrNull { it.storageValue == value } ?: DARK
    }
}
