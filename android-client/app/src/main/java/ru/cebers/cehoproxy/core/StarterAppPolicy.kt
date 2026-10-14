package ru.cebers.cehoproxy.core

object StarterAppPolicy {
    private val recommendedPackages = listOf(
        "com.android.chrome",
        "org.mozilla.firefox",
        "com.microsoft.emmx",
        "org.telegram.messenger",
        "com.whatsapp",
        "org.thoughtcrime.securesms",
        "com.google.android.youtube",
        "com.openai.chatgpt",
        "com.google.android.apps.bard",
        "com.anthropic.claude",
        "com.discord",
    )

    fun installed(installedPackages: Collection<String>): List<String> {
        val installed = installedPackages.toSet()
        return recommendedPackages.filter(installed::contains)
    }
}
