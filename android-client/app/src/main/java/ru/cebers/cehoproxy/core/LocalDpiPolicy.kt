package ru.cebers.cehoproxy.core

object LocalDpiPolicy {
    private const val CLIENT_PACKAGE = "ru.cebers.cehoproxy"
    private val packagePattern = Regex("[A-Za-z0-9_]+(?:\\.[A-Za-z0-9_]+)*")

    fun normalize(packages: List<String>): List<String> = packages.map(String::trim)
        .filter { it != CLIENT_PACKAGE && packagePattern.matches(it) }.distinct()

    fun activeApps(profile: ClientProfile): List<String> =
        if (profile.importedConfig != null) emptyList()
        else activeApps(profile.localDpiApps, profile.apps, profile.excludeSelectedApps)

    fun activeApps(packages: List<String>, selectedApps: List<String>, excludeSelectedApps: Boolean): List<String> =
        normalize(packages).filter { if (excludeSelectedApps) it !in selectedApps else it in selectedApps }
}
