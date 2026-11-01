package ru.cebers.cehoproxy.core

/** Turns one app on or off for the tunnel in either list mode: the list holds tunnelled apps, or apps kept outside. */
internal object AppRoutePolicy {
    fun viaVpn(profile: ClientProfile, packageName: String): Boolean =
        if (profile.excludeSelectedApps) packageName !in profile.apps else packageName in profile.apps || packageName in profile.appNodeRoutes

    fun setViaVpn(profile: ClientProfile, packageName: String, viaVpn: Boolean): ClientProfile {
        val listed = if (profile.excludeSelectedApps) !viaVpn else viaVpn
        val apps = if (listed) (profile.apps + packageName).distinct() else profile.apps - packageName
        val routes = if (!profile.excludeSelectedApps && !viaVpn) profile.appNodeRoutes - packageName else profile.appNodeRoutes
        return profile.copy(apps = apps, appNodeRoutes = routes)
    }
}
