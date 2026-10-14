package ru.cebers.cehoproxy.core

import org.junit.Assert.assertEquals
import org.junit.Test

class SubscriptionInputTest {
    @Test fun httpsNaiveCredentialsAreADirectNode() {
        val link = "https://user:pass@site.example:8443/"
        assertEquals(SubscriptionInputKind.DIRECT_NODE, SubscriptionInput.classify(link))
        assertEquals("naive", NodeShareParser.parse(link).single().type)
    }

    @Test fun plainHttpsAddressRemainsRemoteSubscription() {
        assertEquals(SubscriptionInputKind.REMOTE_SUBSCRIPTION, SubscriptionInput.classify("https://sub.example/list"))
    }

    @Test fun atSignInQueryDoesNotMakeAddressNaiveNode() {
        assertEquals(SubscriptionInputKind.REMOTE_SUBSCRIPTION, SubscriptionInput.classify("https://sub.example/list?account=user@example.com"))
    }

    @Test fun explicitHttpProxyAddressIsNotFetchedAsSubscription() {
        assertEquals(SubscriptionInputKind.DIRECT_NODE, SubscriptionInput.classify("http://user:pass@proxy.example:8080/"))
        assertEquals(SubscriptionInputKind.REMOTE_SUBSCRIPTION, SubscriptionInput.classify("http://sub.example/list"))
    }
}
