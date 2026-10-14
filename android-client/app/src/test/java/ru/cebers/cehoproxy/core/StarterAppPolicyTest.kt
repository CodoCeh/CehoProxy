package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class StarterAppPolicyTest {
    @Test fun starterSetContainsOnlyRecommendedAppsThatAreInstalled() {
        assertEquals(
            listOf("com.android.chrome", "org.telegram.messenger", "com.openai.chatgpt"),
            StarterAppPolicy.installed(listOf("com.openai.chatgpt", "com.android.chrome", "com.example.bank", "org.telegram.messenger")),
        )
    }

    @Test fun starterSetIsEmptyWhenNoRecommendedAppIsInstalled() {
        assertEquals(emptyList<String>(), StarterAppPolicy.installed(listOf("com.example.bank", "com.android.settings")))
    }
}
