package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class RemovedAiModeTest {
    @Test fun profileSavedWithAiModeOnStillLoadsAndDropsTheFlag() {
        val saved = org.json.JSONObject(ProfileCodec.encode(ClientProfile(apps = listOf("a.b"), autoRotationEnabled = true)))
            .put("aiMode", true).toString()
        val loaded = ProfileCodec.decode(saved)
        assertEquals(listOf("a.b"), loaded.apps)
        assertEquals(true, loaded.autoRotationEnabled)
        assertFalse(ProfileCodec.encode(loaded).contains("aiMode"))
    }
}
