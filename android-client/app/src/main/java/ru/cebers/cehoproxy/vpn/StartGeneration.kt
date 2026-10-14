package ru.cebers.cehoproxy.vpn

/** Rejects completion callbacks from starts superseded by a newer start or stop. */
internal class StartGeneration {
    private var value = 0L

    @Synchronized
    fun beginStart(): Long = ++value

    @Synchronized
    fun invalidate(): Long = ++value

    @Synchronized
    fun isCurrent(generation: Long): Boolean = value == generation

    @Synchronized
    fun runIfCurrent(generation: Long, action: () -> Unit): Boolean {
        if (value != generation) return false
        action()
        return true
    }
}
