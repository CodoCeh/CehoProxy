package ru.cebers.cehoproxy.vpn

import org.junit.Assert.*
import org.junit.Test

class TileAnimationPolicyTest {
    @Test fun activationWindowIncludes1999ButEndsAt2000Milliseconds() {
        val policy = TileAnimationPolicy()
        assertNull(policy.remainingMillis(0))
        assertTrue(policy.beginActivation(0))
        assertEquals(2000L, policy.remainingMillis(0))
        assertEquals(1L, policy.remainingMillis(1999))
        assertNull(policy.remainingMillis(2000))
        assertNull(policy.remainingMillis(2001))
    }

    @Test fun animatedDrawableIsPublishedExactlyOncePerActivation() {
        val policy = TileAnimationPolicy()
        policy.beginActivation(100)
        assertTrue(policy.takeAnimationPublication(100))
        assertFalse(policy.takeAnimationPublication(600))
        assertFalse(policy.takeAnimationPublication(2099))
        assertFalse(policy.takeAnimationPublication(2100))
        policy.resetActivation()
        policy.beginActivation(2200)
        assertTrue(policy.takeAnimationPublication(2200))
    }

    @Test fun duplicateActivationNotificationsCannotRestartWindow() {
        val policy = TileAnimationPolicy()
        assertTrue(policy.beginActivation(0))
        assertFalse(policy.beginActivation(900))
        assertEquals(1000L, policy.remainingMillis(1000))
        assertNull(policy.remainingMillis(2000))
        assertFalse(policy.beginActivation(2100))
        assertNull(policy.remainingMillis(2100))
    }

    @Test fun closingAndReopeningPanelCannotRestartCancelledWindow() {
        val policy = TileAnimationPolicy()
        policy.beginActivation(0)
        policy.cancelWindow()
        assertNull(policy.remainingMillis(500))
        assertFalse(policy.beginActivation(500))
        assertFalse(policy.takeAnimationPublication(500))
    }

    @Test fun systemDisabledAnimationsKeepsStaticAndCannotStartCallbacks() {
        val policy = TileAnimationPolicy()
        assertFalse(policy.beginActivation(0, animationsEnabled = false))
        assertNull(policy.remainingMillis(0))
        assertFalse(policy.takeAnimationPublication(0))
        assertFalse(policy.beginActivation(100, animationsEnabled = true))
    }

    @Test fun errorCancelsAndTurningOffAllowsOneNewActivation() {
        val policy = TileAnimationPolicy()
        policy.beginActivation(0)
        policy.cancelWindow()
        assertNull(policy.remainingMillis(1))
        assertFalse(policy.beginActivation(10))
        policy.resetActivation()
        assertTrue(policy.beginActivation(20))
        assertTrue(policy.takeAnimationPublication(20))
        assertNull(policy.remainingMillis(2020))
    }
}
