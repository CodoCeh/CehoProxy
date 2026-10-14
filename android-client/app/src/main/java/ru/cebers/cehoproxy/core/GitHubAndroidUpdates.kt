package ru.cebers.cehoproxy.core

import org.json.JSONArray
import java.net.URI

/** Android versions come from APK names/archives; repository tags also contain desktop releases. */
object GitHubAndroidUpdates {
    const val API = "https://api.github.com/repos/CodoCeh/CehoProxy/releases"
    const val MAX_APK_BYTES = 200L * 1024 * 1024
    data class Asset(val name: String, val url: String, val size: Long, val digest: String?, val versionName: String?)

    fun trustedUrl(url: String, redirected: Boolean = false): Boolean = runCatching {
        val uri = URI(url)
        uri.scheme == "https" && uri.userInfo == null && (uri.port == -1 || uri.port == 443) &&
            if (redirected) uri.host in setOf("github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com")
            else uri.host == "github.com" && uri.path.startsWith("/CodoCeh/CehoProxy/releases/download/")
    }.getOrDefault(false)

    fun parse(json: String, supportedAbis: List<String>): List<Asset> {
        val releases = JSONArray(json)
        return buildList {
            for (i in 0 until releases.length()) {
                val release = releases.getJSONObject(i)
                if (release.optBoolean("draft") || release.optBoolean("prerelease")) continue
                val assets = release.optJSONArray("assets") ?: continue
                val candidates = buildList {
                    for (j in 0 until assets.length()) {
                        val asset = assets.getJSONObject(j)
                        val name = asset.optString("name")
                        val url = asset.optString("browser_download_url")
                        val size = asset.optLong("size", -1)
                        if (!name.endsWith(".apk", ignoreCase = true) || !trustedUrl(url) || size !in 1..MAX_APK_BYTES) continue
                        val version = Regex("(?i)android[-_ ]v?(\\d+\\.\\d+\\.\\d+)(?:[-_.]|$)").find(name)?.groupValues?.get(1)
                        add(Asset(name, url, size, asset.optString("digest").takeIf { it.startsWith("sha256:") }, version))
                    }
                }
                val knownAbis = listOf("arm64-v8a", "armeabi-v7a", "x86_64", "x86")
                fun abiOf(name: String) = knownAbis.firstOrNull { name.contains(it, true) }
                val selected = supportedAbis.firstNotNullOfOrNull { abi -> candidates.firstOrNull { abiOf(it.name) == abi } }
                    ?: candidates.firstOrNull { candidate ->
                        listOf("arm64-v8a", "armeabi-v7a", "x86_64", "x86").none { candidate.name.contains(it, true) }
                    }
                if (selected != null) add(selected)
            }
        }
    }

    fun newerAndroidVersion(candidate: String, installed: String): Boolean? {
        fun parts(version: String) = version.takeIf { it.matches(Regex("\\d+\\.\\d+\\.\\d+")) }
            ?.split('.')?.map { it.toLongOrNull() ?: return null }
        val proposed = parts(candidate) ?: return null
        val current = parts(installed) ?: return null
        for (index in proposed.indices) if (proposed[index] != current[index]) return proposed[index] > current[index]
        return false
    }

    fun validateArchive(installedPackage: String, candidatePackage: String, installedCode: Long, candidateCode: Long,
                        installedSigners: Set<String>, candidateSigners: Set<String>) {
        require(candidatePackage == installedPackage) { "APK другого приложения. Установка отменена." }
        require(installedSigners.isNotEmpty() && candidateSigners == installedSigners) {
            "Подпись APK не совпадает с установленной сборкой. Для тестовой сборки нужен APK с тем же ключом. Установка отменена."
        }
        require(candidateCode > installedCode) { "Установлена эта или более новая версия. Обновление не требуется." }
    }

    fun httpError(code: Int): String = when (code) {
        403, 429 -> "GitHub ограничил частоту запросов. Повторите проверку позже."
        404 -> "Релизы CehoProxy на GitHub недоступны."
        else -> "GitHub ответил HTTP $code. Повторите позже."
    }
}
