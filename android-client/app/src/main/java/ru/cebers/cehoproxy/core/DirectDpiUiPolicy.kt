package ru.cebers.cehoproxy.core

internal object DirectDpiUiPolicy {
    fun setMode(profile: ClientProfile, packageName: String, direct: Boolean): ClientProfile {
        if (profile.importedConfig != null || packageName !in LocalDpiPolicy.normalize(listOf(packageName))) return profile
        return if (direct) profile.copy(
            apps = if (profile.excludeSelectedApps) profile.apps - packageName else (profile.apps + packageName).distinct(),
            localDpiApps = (profile.localDpiApps + packageName).distinct(),
            appNodeRoutes = profile.appNodeRoutes - packageName,
        ) else profile.copy(localDpiApps = profile.localDpiApps - packageName)
    }
}
