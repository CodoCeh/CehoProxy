package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test

class AiNodePolicyTest {
    @Test fun countriesWhereGoogleDoesNotServeGeminiAreRefused() {
        listOf("RU", "ru", "CN", "IR", "BY", "HK").forEach { assertFalse(it, AiNodePolicy.countrySupported(it)) }
        listOf("DE", "FI", "NL", "US", "TR", "KZ", "EE").forEach { assertTrue(it, AiNodePolicy.countrySupported(it)) }
    }

    @Test fun unknownExitCountryIsNotTrusted() {
        assertFalse(AiNodePolicy.countrySupported(null))
        assertFalse(AiNodePolicy.countrySupported(""))
    }

    @Test fun nodeNeedsBothAServedCountryAndAReachableGoogle() {
        assertTrue(AiNodePolicy.allows("DE", true))
        assertFalse(AiNodePolicy.allows("DE", false))
        assertFalse(AiNodePolicy.allows("RU", true))
    }

    @Test fun aiModeSurvivesSaveAndLoad() {
        val loaded = ProfileCodec.decode(ProfileCodec.encode(ClientProfile(aiMode = true, autoRotationEnabled = true)))
        assertTrue(loaded.aiMode)
        assertEquals(false, ProfileCodec.decode(ProfileCodec.encode(ClientProfile())).aiMode)
    }

    @Test fun liveProbeThroughALocalProxyPort() {
        val port = (System.getenv("CEHO_AI_PROBE_PORT") ?: java.io.File("/tmp/x/aiport").takeIf { it.exists() }?.readText())?.trim()?.toIntOrNull()
        assumeTrue(port != null)
        val country = NodeExitCountryProbe.fetch(port!!)?.countryCode
        val reachable = AiNodePolicy.googleReachable(port)
        println("AI-PROBE port=$port country=$country google=$reachable allows=${AiNodePolicy.allows(country, reachable)}")
    }
}
