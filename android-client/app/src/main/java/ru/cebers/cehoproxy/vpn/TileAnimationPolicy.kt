package ru.cebers.cehoproxy.vpn

internal class TileAnimationPolicy {
    companion object {
        const val DURATION_MS = 2000L
    }

    private var startedAt: Long? = null
    private var activationConsumed = false
    private var animationPublished = false

    fun beginActivation(now: Long, animationsEnabled: Boolean = true): Boolean {
        if (activationConsumed) return false
        activationConsumed = true
        if (!animationsEnabled) return false
        startedAt = now
        animationPublished = false
        return true
    }

    fun takeAnimationPublication(now: Long): Boolean {
        if (remainingMillis(now) == null || animationPublished) return false
        animationPublished = true
        return true
    }

    fun remainingMillis(now: Long): Long? {
        val start = startedAt ?: return null
        val elapsed = now - start
        if (elapsed !in 0 until DURATION_MS) {
            startedAt = null
            return null
        }
        return DURATION_MS - elapsed
    }

    fun cancelWindow() { startedAt = null }

    fun resetActivation() {
        cancelWindow()
        activationConsumed = false
    }
}
