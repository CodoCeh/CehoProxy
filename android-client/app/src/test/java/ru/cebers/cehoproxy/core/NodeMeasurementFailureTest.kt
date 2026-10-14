package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class NodeMeasurementFailureTest {
    @Test fun tlsAndTimeoutFailuresRetainUsefulCategoryAndNativeStage() {
        val tls = NodeMeasurementFailure.from("fetch config: Get https://test:443/config: tls: failed to verify certificate: x509: certificate signed by unknown authority")
        assertEquals("fetch_config", tls.stage)
        assertEquals("untrusted_certificate", tls.category)
        assertEquals("Сертификат сервера проверки не доверен", tls.description)
        val timeout = NodeMeasurementFailure.from("measure idle latency: context deadline exceeded")
        assertEquals("latency", timeout.stage)
        assertEquals("timeout", timeout.category)
    }

    @Test fun diagnosticNeverCopiesCredentialsUrlsOrArbitraryNativePayload() {
        val secret = "secret-do-not-copy"
        val failure = NodeMeasurementFailure.from("measure download: Get https://user:$secret@private.example/secret?token=$secret: unexpected status: 403 Forbidden payload=$secret")
        assertEquals("download", failure.stage)
        assertEquals(403, failure.httpStatus)
        assertEquals("http_status", failure.category)
        assertFalse((failure.safeLog + failure.description).contains(secret))
        assertFalse(failure.safeLog.contains("private.example"))
        assertFalse(failure.safeLog.contains("https://"))
        val unknown = NodeMeasurementFailure.from("password=$secret custom-server-payload=$secret")
        assertEquals("stage=start_or_stream category=unknown", unknown.safeLog)
        assertFalse(unknown.description.contains(secret))
    }

    @Test fun categorizedFailureAppearsInMetricsWithoutRawError() {
        val failure = NodeMeasurementFailure.from("fetch config: lookup private.example: no such host")
        assertEquals("Не удалось найти сервер проверки", NodeMetricsPolicy.summary(NodeMeasurement(failed = true, failureReason = failure.description)))
    }
}
