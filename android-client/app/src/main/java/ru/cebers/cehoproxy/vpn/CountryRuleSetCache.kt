package ru.cebers.cehoproxy.vpn

import android.content.Context
import org.json.JSONObject
import ru.cebers.cehoproxy.core.CountryRouting
import ru.cebers.cehoproxy.core.CountryRuleSet
import java.io.File
import java.io.FileOutputStream
import java.security.MessageDigest

internal object CountryRuleSetCache {
    @Synchronized fun prepare(context: Context, countryCode: String): List<CountryRuleSet> {
        if (countryCode.isBlank()) return emptyList()
        val country = CountryRouting.find(countryCode) ?: error("Набор правил выбранной страны недоступен")
        val manifestBytes = context.assets.open("country-rules/manifest.json").use { it.readBytes() }
        val files = JSONObject(String(manifestBytes, Charsets.UTF_8)).getJSONArray("files")
        val expected = (0 until files.length()).map { files.getJSONObject(it) }
            .associate { it.getString("name") to it.getString("sha256") }
        val directory = File(context.filesDir, "country-rules/${sha256(manifestBytes).take(16)}")
        check(directory.isDirectory || directory.mkdirs()) { "Не удалось создать кэш правил страны" }
        return listOfNotNull(country.ipAsset, country.domainAsset).map { name ->
            val checksum = expected[name] ?: error("В пакете нет правил страны $name")
            val file = File(directory, name)
            if (!file.isFile || sha256(file.readBytes()) != checksum) {
                val data = context.assets.open("country-rules/$name").use { it.readBytes() }
                check(sha256(data) == checksum) { "Повреждён набор правил страны $name" }
                val temporary = File.createTempFile("country-rule-", ".srs", directory)
                try {
                    FileOutputStream(temporary).use { output -> output.write(data); output.fd.sync() }
                    check(temporary.renameTo(file)) { "Не удалось сохранить правила страны $name" }
                } finally { temporary.delete() }
            }
            val domains = name == country.domainAsset
            CountryRuleSet("country-${country.code}-${if (domains) "domains" else "ip"}", file.absolutePath, domains)
        }
    }
    private fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes)
        .joinToString("") { "%02x".format(it) }
}
