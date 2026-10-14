package ru.cebers.cehoproxy.vpn

import android.content.Context
import android.util.Base64
import android.util.Log
import ru.cebers.cehoproxy.core.NativeTrustConfig
import java.io.File
import java.security.KeyStore
import java.security.MessageDigest

internal object AndroidSystemTrustBundle {
    data class Bundle(val file: File, val certificateCount: Int, val sha256: String)

    fun applyTo(context: Context, config: String): String =
        NativeTrustConfig.applyAndroidSystemTrust(config) { export(context).file.absolutePath }

    @Synchronized fun export(context: Context): Bundle {
        val store = KeyStore.getInstance("AndroidCAStore").apply { load(null) }
        val aliases = store.aliases().toList().filter { it.startsWith("system:") }.sorted()
        val certificates = aliases.mapNotNull { store.getCertificate(it) }
        check(certificates.isNotEmpty()) { "Android не предоставил системные корневые сертификаты" }
        val pem = certificates.joinToString("") { certificate ->
            val encoded = Base64.encodeToString(certificate.encoded, Base64.NO_WRAP)
            "-----BEGIN CERTIFICATE-----\n" + encoded.chunked(64).joinToString("\n") +
                "\n-----END CERTIFICATE-----\n"
        }
        val file = File(context.filesDir, "android-system-ca.pem")
        val temporary = File.createTempFile("android-system-ca-", ".pem", context.filesDir)
        try {
            temporary.writeText(pem, Charsets.US_ASCII)
            check(temporary.renameTo(file)) { "Не удалось сохранить системные сертификаты Android" }
        } finally {
            temporary.delete()
        }
        val sha256 = MessageDigest.getInstance("SHA-256").digest(pem.toByteArray(Charsets.US_ASCII))
            .joinToString("") { "%02x".format(it) }
        Log.i("CehoSystemTrust", "System CA count=${certificates.size}; sha256=$sha256")
        return Bundle(file, certificates.size, sha256)
    }
}
