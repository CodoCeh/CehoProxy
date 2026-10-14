package ru.cebers.cehoproxy.vpn

/** Validates the app scope before it is applied to Android's VPN builder. */
internal object AppScopePolicy {
    fun validate(selectedPackages: List<String>, configuredPackages: List<String>, excludeSelectedApps: Boolean = false): String? {
        if (selectedPackages.isEmpty() && !excludeSelectedApps) {
            return "Выберите хотя бы одно приложение для туннеля. Остальные приложения останутся напрямую"
        }
        if (selectedPackages.any(String::isBlank) || configuredPackages.any(String::isBlank)) {
            return "Список приложений для VPN содержит пустое имя пакета"
        }
        if (selectedPackages.toSet() != configuredPackages.toSet()) {
            return "Не удалось применить выбранные приложения к VPN. Проверьте, что приложения установлены, и выберите их снова"
        }
        return null
    }
}
