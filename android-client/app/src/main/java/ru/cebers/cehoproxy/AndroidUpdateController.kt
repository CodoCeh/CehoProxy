package ru.cebers.cehoproxy

import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import ru.cebers.cehoproxy.core.GitHubAndroidUpdates
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.zip.ZipFile

internal data class AndroidUpdateState(
    val automatic: Boolean = true,
    val busy: Boolean = false,
    val progress: Int? = null,
    val lastChecked: Long = 0,
    val message: String = "Проверка ещё не выполнялась",
    val candidate: GitHubAndroidUpdates.Asset? = null,
    val ready: File? = null,
)

internal class AndroidUpdateController private constructor(private val context: Context) {
    private val preferences = context.getSharedPreferences("android_updates", Context.MODE_PRIVATE)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val mutable = MutableStateFlow(AndroidUpdateState(automatic = preferences.getBoolean("automatic", true), lastChecked = preferences.getLong("last_checked", 0)))
    val state = mutable.asStateFlow()

    fun automatic(enabled: Boolean) {
        preferences.edit().putBoolean("automatic", enabled).apply()
        mutable.value = mutable.value.copy(automatic = enabled)
        if (enabled) checkIfDue()
    }

    fun checkIfDue() {
        val previous = preferences.getLong("last_attempt", 0)
        val now = System.currentTimeMillis()
        if (mutable.value.automatic && (previous == 0L || now - previous >= 24 * 60 * 60 * 1000L || now < previous)) check()
    }

    @Synchronized fun check() {
        if (mutable.value.busy) return
        val now = System.currentTimeMillis()
        preferences.edit().putLong("last_attempt", now).apply()
        mutable.value = mutable.value.copy(busy = true, progress = null, message = "Проверяем GitHub…")
        scope.launch {
            runCatching {
                var candidate: GitHubAndroidUpdates.Asset? = null
                for (page in 1..5) {
                    val json = readCatalog("${GitHubAndroidUpdates.API}?per_page=30&page=$page")
                    candidate = GitHubAndroidUpdates.parse(json, Build.SUPPORTED_ABIS.toList()).firstOrNull()
                    if (candidate != null || org.json.JSONArray(json).length() < 30) break
                }
                val namedVersion = candidate?.versionName
                val alreadyCurrent = namedVersion != null && GitHubAndroidUpdates.newerAndroidVersion(namedVersion, BuildConfig.VERSION_NAME) == false
                if (alreadyCurrent) candidate = null
                // Even an Android filename is not proof of upgrade; archive versionCode is authoritative.
                val message = if (alreadyCurrent) "Установлена актуальная Android-версия ${BuildConfig.VERSION_NAME}."
                    else if (candidate == null) "На GitHub пока нет подходящего Android APK. Установлена ${BuildConfig.VERSION_NAME}."
                    else "Android APK найден${candidate.versionName?.let { ": $it" } ?: ""}. После загрузки проверим версию и подпись."
                preferences.edit().putLong("last_checked", now).apply()
                mutable.value = mutable.value.copy(busy = false, lastChecked = now, candidate = candidate, message = message)
            }.onFailure { mutable.value = mutable.value.copy(busy = false, message = readable(it)) }
        }
    }

    @Synchronized fun download() {
        if (mutable.value.busy) return
        val asset = mutable.value.candidate ?: return
        mutable.value = mutable.value.copy(busy = true, progress = 0, ready = null, message = "Загружаем Android APK…")
        scope.launch {
            val directory = File(context.cacheDir, "updates").apply { mkdirs() }
            val staging = File(directory, "update.part")
            val ready = File(directory, "update.apk")
            runCatching {
                staging.delete()
                ready.delete()
                val connection = assetConnection(asset.url)
                try {
                    val expected = connection.contentLengthLong
                    require(expected <= GitHubAndroidUpdates.MAX_APK_BYTES && (expected < 0 || expected == asset.size)) { "Размер APK не совпадает с релизом." }
                    connection.inputStream.use { input -> staging.outputStream().use { output ->
                        val buffer = ByteArray(64 * 1024)
                        var total = 0L
                        val started = System.nanoTime()
                        while (true) {
                            require((System.nanoTime() - started) / 1_000_000_000 < 600) { "Истекло время загрузки APK. Повторите позже." }
                            val count = input.read(buffer)
                            if (count < 0) break
                            total += count
                            require(total <= asset.size && total <= GitHubAndroidUpdates.MAX_APK_BYTES) { "APK превышает заявленный размер." }
                            output.write(buffer, 0, count)
                            mutable.value = mutable.value.copy(progress = ((total * 100) / asset.size).toInt())
                        }
                        require(total == asset.size) { "APK загружен не полностью. Повторите загрузку." }
                    } }
                } finally { connection.disconnect() }
                asset.digest?.let { require("sha256:${fileSha256(staging)}" == it) { "Контрольная сумма APK не совпадает. Установка отменена." } }
                validate(staging)
                require(staging.renameTo(ready)) { "Не удалось сохранить APK." }
                mutable.value = mutable.value.copy(busy = false, progress = null, ready = ready, message = "APK проверен. Подтвердите обновление в системном установщике Android.")
            }.onFailure {
                staging.delete()
                ready.delete()
                mutable.value = mutable.value.copy(busy = false, progress = null, ready = null, message = readable(it))
            }
        }
    }

    fun install(activityContext: Context) {
        val file = mutable.value.ready ?: return
        runCatching {
            validate(file)
            if (!context.packageManager.canRequestPackageInstalls()) {
                mutable.value = mutable.value.copy(message = "Разрешите установку из CehoProxy в Android, вернитесь и нажмите «Установить».")
                activityContext.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}")))
                return
            }
            val uri = FileProvider.getUriForFile(context, "${context.packageName}.updates", file)
            activityContext.startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
        }.onFailure { mutable.value = mutable.value.copy(message = readable(it)) }
    }

    private fun validate(file: File) {
        val manager = context.packageManager
        val flags = PackageManager.GET_SIGNING_CERTIFICATES
        @Suppress("DEPRECATION") val installed = manager.getPackageInfo(context.packageName, flags)
        @Suppress("DEPRECATION") val archive = manager.getPackageArchiveInfo(file.absolutePath, flags) ?: error("Android не распознал APK. Установка отменена.")
        GitHubAndroidUpdates.validateArchive(context.packageName, archive.packageName, installed.longVersionCode,
            archive.longVersionCode, signers(installed), signers(archive))
        ZipFile(file).use { zip ->
            val nativeAbis = zip.entries().asSequence().map { it.name }.filter { it.startsWith("lib/") && it.endsWith(".so") }
                .map { it.split('/')[1] }.toSet()
            require(nativeAbis.isEmpty() || Build.SUPPORTED_ABIS.any { it in nativeAbis }) { "APK не подходит процессору устройства." }
        }
    }

    private fun fileSha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(64 * 1024)
            while (true) { val count = input.read(buffer); if (count < 0) break; digest.update(buffer, 0, count) }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }
    private fun signers(info: PackageInfo): Set<String> = info.signingInfo?.apkContentsSigners?.map { sha256(it.toByteArray()) }?.toSet().orEmpty()
    private fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }

    private fun connection(url: String) = (URL(url).openConnection() as HttpURLConnection).apply {
        instanceFollowRedirects = false
        connectTimeout = 15_000
        readTimeout = 30_000
        setRequestProperty("User-Agent", "CehoProxy-Android/${BuildConfig.VERSION_NAME}")
        setRequestProperty("Accept", "application/vnd.github+json")
    }
    private fun assetConnection(initial: String): HttpURLConnection {
        require(GitHubAndroidUpdates.trustedUrl(initial)) { "Недоверенная ссылка APK." }
        var current = initial
        repeat(6) {
            val connection = connection(current)
            val code = connection.responseCode
            if (code in listOf(301, 302, 303, 307, 308)) {
                val location = connection.getHeaderField("Location")
                connection.disconnect()
                require(!location.isNullOrBlank()) { "GitHub вернул пустое перенаправление." }
                current = URL(URL(current), location).toString()
                require(GitHubAndroidUpdates.trustedUrl(current, redirected = true)) { "Недоверенное перенаправление APK." }
            } else {
                if (code != 200) { connection.disconnect(); error(GitHubAndroidUpdates.httpError(code)) }
                return connection
            }
        }
        error("Слишком много перенаправлений GitHub.")
    }
    private fun readCatalog(url: String): String {
        val connection = connection(url)
        try {
            if (connection.responseCode != 200) error(GitHubAndroidUpdates.httpError(connection.responseCode))
            return connection.inputStream.use { stream ->
                val started = System.nanoTime()
                val output = java.io.ByteArrayOutputStream()
                val buffer = ByteArray(16 * 1024)
                while (true) {
                    require((System.nanoTime() - started) / 1_000_000_000 < 60) { "Истекло время проверки GitHub. Повторите позже." }
                    val count = stream.read(buffer)
                    if (count < 0) break
                    require(output.size() + count <= 2 * 1024 * 1024) { "Ответ GitHub слишком большой." }
                    output.write(buffer, 0, count)
                }
                output.toString("UTF-8")
            }
        } finally { connection.disconnect() }
    }
    private fun readable(error: Throwable) = when (error) {
        is java.net.UnknownHostException -> "Не удалось подключиться к GitHub. Проверьте интернет."
        is java.net.SocketTimeoutException -> "GitHub не ответил вовремя. Повторите позже."
        is org.json.JSONException -> "Не удалось прочитать список релизов GitHub. Повторите позже."
        is android.content.ActivityNotFoundException -> "На устройстве недоступен системный установщик Android."
        is java.io.IOException -> "Ошибка загрузки с GitHub. Проверьте интернет и свободное место."
        else -> error.message ?: "Не удалось проверить обновление."
    }

    companion object {
        @Volatile private var instance: AndroidUpdateController? = null
        fun get(context: Context): AndroidUpdateController = instance ?: synchronized(this) {
            instance ?: AndroidUpdateController(context.applicationContext).also { instance = it }
        }
    }
}
