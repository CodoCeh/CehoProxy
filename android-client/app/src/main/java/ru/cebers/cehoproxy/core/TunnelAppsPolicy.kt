package ru.cebers.cehoproxy.core

object TunnelAppsPolicy {
    /** Packages whose traffic goes through the tunnel. Apps that go direct (DPI bypass, YouTube ad block) are left out. */
    fun packages(profile: ClientProfile, installed: List<String>): List<String> {
        val routed = if (profile.excludeSelectedApps) installed.filterNot { it in profile.apps }
        else (profile.apps + profile.appNodeRoutes.keys).distinct()
        val direct = LocalDpiPolicy.activeApps(profile.localDpiApps, profile.apps, profile.excludeSelectedApps).toSet() +
            setOfNotNull(YoutubeAdPolicy.PACKAGE.takeIf { profile.blockYoutubeAds })
        return routed.filterNot { it in direct }
    }
}
