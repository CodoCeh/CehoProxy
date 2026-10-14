package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class GitHubAndroidUpdatesTest {
    private fun release(assets: String, flags: String = "", tag: String = "v1.2.999") = """[{"tag_name":"$tag",$flags"assets":[$assets]}]"""
    private fun asset(name: String) = """{"name":"$name","size":100,"browser_download_url":"https://github.com/CodoCeh/CehoProxy/releases/download/v1/$name"}"""
    @Test fun desktopTagDoesNotBecomeAndroidVersion() {
        assertNull(GitHubAndroidUpdates.parse(release(asset("CehoProxy-Android-arm64-v8a.apk")), listOf("arm64-v8a")).single().versionName)
        assertTrue(GitHubAndroidUpdates.parse(release(asset("CehoProxy-linux.apk.exe")), listOf("arm64-v8a")).isEmpty())
    }
    @Test fun androidFilenameVersionAndAbiPreference() {
        val result = GitHubAndroidUpdates.parse(release(listOf(asset("CehoProxy-Android-0.1.4-x86_64.apk"), asset("CehoProxy-Android-0.1.4-arm64-v8a.apk")).joinToString()), listOf("arm64-v8a", "x86_64")).single()
        assertEquals("0.1.4", result.versionName)
        assertTrue(result.name.contains("arm64-v8a"))
    }
    @Test fun incompatibleAbiAndUnpublishedReleasesIgnored() {
        assertTrue(GitHubAndroidUpdates.parse(release(asset("CehoProxy-Android-x86_64.apk")), listOf("arm64-v8a")).isEmpty())
        for (flag in listOf("\"draft\":true,", "\"prerelease\":true,"))
            assertTrue(GitHubAndroidUpdates.parse(release(asset("CehoProxy-Android.apk"), flag), listOf("arm64-v8a")).isEmpty())
    }
    @Test fun requiresOfficialHttpsAndBoundedAssets() {
        assertFalse(GitHubAndroidUpdates.trustedUrl("http://github.com/CodoCeh/CehoProxy/releases/download/a/b.apk"))
        assertFalse(GitHubAndroidUpdates.trustedUrl("https://github.com/evil/repo/releases/download/a/b.apk"))
        assertFalse(GitHubAndroidUpdates.trustedUrl("https://evil.example/a", true))
        assertTrue(GitHubAndroidUpdates.trustedUrl("https://release-assets.githubusercontent.com/a", true))
        assertTrue(GitHubAndroidUpdates.parse(release(asset("app.apk").replace("100", "999999999")), listOf("arm64-v8a")).isEmpty())
    }
    @Test fun androidVersionUsesNumbersAndIgnoresDesktopTags() {
        assertEquals(true, GitHubAndroidUpdates.newerAndroidVersion("0.1.10", "0.1.9"))
        assertEquals(false, GitHubAndroidUpdates.newerAndroidVersion("0.1.3", "0.1.4"))
        assertEquals(false, GitHubAndroidUpdates.newerAndroidVersion("0.1.4", "0.1.4"))
        assertNull(GitHubAndroidUpdates.newerAndroidVersion("v1.2.999", "0.1.4"))
        assertTrue(GitHubAndroidUpdates.parse(release(asset("CehoProxy-Android-x86_64.apk")), listOf("x86")).isEmpty())
    }
    @Test fun signaturePackageAndDowngradeFailClosed() {
        GitHubAndroidUpdates.validateArchive("app", "app", 4, 5, setOf("key"), setOf("key"))
        for (candidate in listOf(Triple("other", 5L, setOf("key")), Triple("app", 4L, setOf("key")), Triple("app", 3L, setOf("key")), Triple("app", 5L, setOf("debug")), Triple("app", 5L, emptySet()))) {
            assertThrows(IllegalArgumentException::class.java) { GitHubAndroidUpdates.validateArchive("app", candidate.first, 4, candidate.second, setOf("key"), candidate.third) }
        }
    }
    @Test fun serverErrorsAreReadable() {
        assertTrue(GitHubAndroidUpdates.httpError(429).contains("частоту"))
        assertTrue(GitHubAndroidUpdates.httpError(500).contains("500"))
    }
}
