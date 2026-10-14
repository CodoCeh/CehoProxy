package ru.cebers.cehoproxy.core

import java.util.Locale

data class CountryRoutingOption(val code: String, val name: String, val domainAsset: String? = null) {
    val ipAsset: String get() = "geoip-$code.srs"
    val coverage: String get() = if (domainAsset == null) "IP-адреса" else "Домены и IP-адреса"
}

data class CountryRuleSet(val tag: String, val path: String, val domains: Boolean)

object CountryRouting {
    val countries = listOf(
        CountryRoutingOption("ru", "Россия", "geosite-category-ru.srs"),
        CountryRoutingOption("cn", "Китай", "geosite-cn.srs"),
        CountryRoutingOption("ir", "Иран", "geosite-category-ir.srs"),
        CountryRoutingOption("de", "Германия"), CountryRoutingOption("us", "США"),
        CountryRoutingOption("gb", "Великобритания"), CountryRoutingOption("kz", "Казахстан"),
        CountryRoutingOption("by", "Беларусь"), CountryRoutingOption("tr", "Турция"), CountryRoutingOption("ua", "Украина"),
    )
    fun find(code: String): CountryRoutingOption? = countries.firstOrNull { it.code == code.trim().lowercase(Locale.ROOT) }
    fun normalize(code: String): String = find(code)?.code.orEmpty()
}
