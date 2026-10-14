package ru.cebers.cehoproxy.core

internal object YoutubeAdPolicy {
    const val PACKAGE = "com.google.android.youtube"
    val domainSuffixes = listOf("doubleclick.net", "googlesyndication.com", "googleadservices.com", "googletagservices.com", "adservice.google.com")
}
