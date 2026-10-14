package ru.cebers.cehoproxy.core

import java.net.URI

sealed interface QrImportDecision {
    data object NoCode : QrImportDecision
    data object MultipleCodes : QrImportDecision
    data object Unsupported : QrImportDecision
    data class Import(val value: String) : QrImportDecision
}

object QrImportPolicy {
    fun decide(values: List<String?>): QrImportDecision {
        val codes = values.mapNotNull { it?.trim()?.takeIf(String::isNotEmpty) }.distinct()
        if (codes.isEmpty()) return QrImportDecision.NoCode
        if (codes.size > 1) return QrImportDecision.MultipleCodes

        val value = codes.single()
        val uri = runCatching { URI(value) }.getOrNull()
        val isRemoteSubscription = uri != null &&
            uri.scheme?.lowercase() in setOf("http", "https") &&
            !uri.host.isNullOrBlank()
        return if (isRemoteSubscription || NodeShareParser.parse(value).isNotEmpty()) {
            QrImportDecision.Import(value)
        } else QrImportDecision.Unsupported
    }
}
