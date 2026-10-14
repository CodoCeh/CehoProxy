package ru.cebers.cehoproxy.core

import java.util.Locale

internal object NodeCountry {
    private val names: List<Pair<String, String>> by lazy {
        Locale.getISOCountries().flatMap { code ->
            val country = Locale("", code)
            listOf(country.getDisplayCountry(Locale("ru")), country.getDisplayCountry(Locale.ENGLISH))
                .map { it.lowercase(Locale.ROOT) to code }
        }.filter { it.first.length >= 3 }.distinct().sortedByDescending { it.first.length }
    }

    fun resolve(name: String): String? {
        flagCode(name)?.let { return it }
        val lower = name.lowercase(Locale.ROOT)
        for ((needle, code) in names) {
            var at = lower.indexOf(needle)
            while (at >= 0) {
                if (at == 0 || !lower[at - 1].isLetterOrDigit()) return code
                at = lower.indexOf(needle, at + 1)
            }
        }
        return null
    }

    fun displayName(code: String): String = Locale("", code).getDisplayCountry(Locale("ru")).ifBlank { code }

    fun flagCode(name: String): String? {
        val points = name.codePoints().toArray()
        for (i in 0 until points.size - 1) {
            if (points[i] in 0x1F1E6..0x1F1FF && points[i + 1] in 0x1F1E6..0x1F1FF) {
                return String(charArrayOf('A' + (points[i] - 0x1F1E6), 'A' + (points[i + 1] - 0x1F1E6)))
            }
        }
        return null
    }
}
