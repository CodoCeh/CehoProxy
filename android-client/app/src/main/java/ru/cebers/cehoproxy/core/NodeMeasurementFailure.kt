package ru.cebers.cehoproxy.core

internal data class NodeMeasurementFailure(val category: String, val stage: String, val description: String, val httpStatus: Int? = null) {
    val safeLog: String get() = "stage=$stage category=$category" + (httpStatus?.let { " http_status=$it" } ?: "")

    companion object {
        // Native errors may contain subscription URLs or credentials. Log only recognised fields.
        fun from(message: String?): NodeMeasurementFailure {
            val text = message.orEmpty().lowercase()
            val stage = when {
                "fetch config" in text -> "fetch_config"
                "validate config" in text || "decode config" in text -> "validate_config"
                "measure idle latency" in text -> "latency"
                "measure download" in text -> "download"
                "measure upload" in text -> "upload"
                "watchdog" in text -> "watchdog"
                else -> "start_or_stream"
            }
            val httpStatus = Regex("unexpected status: ([1-5][0-9]{2})\\b").find(text)?.groupValues?.get(1)?.toIntOrNull()
            val (category, description) = when {
                "unknown authority" in text -> "untrusted_certificate" to "Сертификат сервера проверки не доверен"
                "certificate" in text && ("not valid for" in text || "doesn't contain" in text || "hostname" in text) -> "certificate_hostname" to "Имя сервера проверки не совпадает с сертификатом"
                "timeout" in text || "deadline exceeded" in text || "timed out" in text -> "timeout" to "Сервер проверки не ответил вовремя"
                "lookup" in text || "dns" in text || "no such host" in text -> "dns" to "Не удалось найти сервер проверки"
                "connection refused" in text || "network is unreachable" in text || "no route to host" in text -> "connection" to "Сервер проверки недоступен"
                "tls" in text || "handshake" in text -> "tls" to "Не удалось установить защищённое соединение"
                httpStatus != null -> "http_status" to "Сервер проверки вернул HTTP $httpStatus"
                "service not started" in text || "service is not started" in text || "outbound not found" in text || "unavailable" in text || "not connected" in text -> "engine_unavailable" to "Движок измерения недоступен"
                "canceled" in text || "cancelled" in text -> "canceled" to "Проверка отменена"
                stage == "validate_config" -> "invalid_config" to "Сервер проверки вернул неподдерживаемые настройки"
                "eof" in text || "connection reset" in text || "broken pipe" in text -> "connection_closed" to "Соединение с сервером проверки прервалось"
                else -> "unknown" to "Измерение недоступно"
            }
            return NodeMeasurementFailure(category, stage, description, httpStatus)
        }
    }
}
