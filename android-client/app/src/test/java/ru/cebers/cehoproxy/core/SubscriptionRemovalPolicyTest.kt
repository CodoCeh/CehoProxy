package ru.cebers.cehoproxy.core

import org.junit.Assert.*
import org.junit.Test

class SubscriptionRemovalPolicyTest {
    private val first = "https://one.example/sub"
    private val second = "https://two.example/sub"
    private fun node(name: String, host: String) = ProxyNode(name, "socks", "$host.server", 1080, "raw-$name", subscriptionMetadata = SubscriptionMetadata(sourceHost = host))
    private val profile = ClientProfile(
        nodes = listOf(node("a1", "one.example"), node("b1", "two.example"), node("a2", "one.example")),
        selected = 1,
        subscriptionUrls = listOf(first, second),
    )

    @Test fun removesUrlAndItsNodesKeepingOthers() {
        val result = SubscriptionRemovalPolicy.remove(profile, first)
        assertEquals(listOf(second), result.subscriptionUrls)
        assertEquals(listOf("b1"), result.nodes.map { it.name })
    }

    @Test fun keepsSelectedNodeWhenItSurvives() {
        val result = SubscriptionRemovalPolicy.remove(profile, first)
        assertEquals(0, result.selected)
        assertEquals("b1", result.selectedNode?.name)
    }

    @Test fun fallsBackToFirstNodeWhenSelectedIsRemoved() {
        val result = SubscriptionRemovalPolicy.remove(profile.copy(selected = 0), first)
        assertEquals(0, result.selected)
        assertEquals("b1", result.selectedNode?.name)
    }

    @Test fun unknownUrlChangesNothing() {
        assertSame(profile, SubscriptionRemovalPolicy.remove(profile, "https://other.example/sub"))
    }

    @Test fun urlForMatchesSourceHost() {
        assertEquals(second, SubscriptionRemovalPolicy.urlFor(profile, SubscriptionMetadata(sourceHost = "two.example")))
        assertNull(SubscriptionRemovalPolicy.urlFor(profile, SubscriptionMetadata()))
    }
}
