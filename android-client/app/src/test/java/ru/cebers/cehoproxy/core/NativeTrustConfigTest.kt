package ru.cebers.cehoproxy.core

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class NativeTrustConfigTest {
    @Test fun defaultNativeTrustUsesOnlyExportedSystemRootsAndPreservesTlsVerification() {
        val input = """{"outbounds":[{"type":"hysteria2","tls":{"enabled":true,"server_name":"edge.example","certificate_public_key_sha256":["pin"]}}]}"""
        val result = JSONObject(NativeTrustConfig.applyAndroidSystemTrust(input) { "/private/system-ca.pem" })
        assertEquals("none", result.getJSONObject("certificate").getString("store"))
        assertEquals("/private/system-ca.pem", result.getJSONObject("certificate").getJSONArray("certificate_path").getString(0))
        val tls = result.getJSONArray("outbounds").getJSONObject(0).getJSONObject("tls")
        assertFalse(tls.has("insecure"))
        assertEquals("pin", tls.getJSONArray("certificate_public_key_sha256").getString(0))
        assertEquals("edge.example", tls.getString("server_name"))
    }

    @Test fun explicitImportedTrustIsPreservedWithoutExportingSystemRoots() {
        for (certificate in listOf("""{"store":"none","certificate_path":["custom.pem"]}""", """{"store":"mozilla"}""", """{"store":"system","certificate":["custom"]}""")) {
            val input = "{\"certificate\":$certificate,\"outbounds\":[]}"
            assertEquals(input, NativeTrustConfig.applyAndroidSystemTrust(input) { error("Explicit imported trust must remain intact") })
        }
    }
}
