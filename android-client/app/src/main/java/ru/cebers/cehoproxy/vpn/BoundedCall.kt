package ru.cebers.cehoproxy.vpn

import java.util.concurrent.Callable
import java.util.concurrent.ExecutionException
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException

/** Runs a native call that may never return (libbox calls have no timeout) and gives up after the limit. */
internal class BoundedCall(threadName: String) {
    private val executor: ExecutorService = Executors.newCachedThreadPool { task ->
        Thread(task, threadName).apply { isDaemon = true }
    }

    /** Returns null when the call fails or does not finish in time. The caller keeps running. */
    fun <T> run(timeoutMillis: Long, block: () -> T): T? {
        val future = executor.submit(Callable { block() })
        return try {
            future.get(timeoutMillis, TimeUnit.MILLISECONDS)
        } catch (_: TimeoutException) {
            future.cancel(true)
            null
        } catch (_: ExecutionException) {
            null
        } catch (_: InterruptedException) {
            Thread.currentThread().interrupt()
            null
        }
    }

    fun shutdown() {
        executor.shutdownNow()
    }
}
