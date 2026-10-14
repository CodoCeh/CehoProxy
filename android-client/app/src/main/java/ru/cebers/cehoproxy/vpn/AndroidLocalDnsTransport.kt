package ru.cebers.cehoproxy.vpn

import android.net.DnsResolver
import android.net.Network
import android.os.CancellationSignal
import android.system.ErrnoException
import io.nekohasekai.libbox.ExchangeContext
import io.nekohasekai.libbox.LocalDNSTransport
import java.util.concurrent.CompletableFuture
import java.util.concurrent.Executor

internal class AndroidLocalDnsTransport(private val network: () -> Network?) : LocalDNSTransport {
    override fun raw() = true

    override fun exchange(ctx: ExchangeContext, message: ByteArray) {
        val upstream = network() ?: error("Нет физической сети для DNS")
        val signal = CancellationSignal()
        val completion = CompletableFuture<Unit>()
        ctx.onCancel {
            signal.cancel()
            completion.cancel(false)
        }
        DnsResolver.getInstance().rawQuery(upstream, message, DnsResolver.FLAG_NO_RETRY,
            Executor { it.run() }, signal, object : DnsResolver.Callback<ByteArray> {
                override fun onAnswer(answer: ByteArray, rcode: Int) {
                    if (completion.isCancelled) return
                    try {
                        if (rcode == 0) ctx.rawSuccess(answer) else ctx.errorCode(rcode)
                        completion.complete(Unit)
                    } catch (error: Throwable) {
                        completion.completeExceptionally(error)
                    }
                }
                override fun onError(error: DnsResolver.DnsException) {
                    if (completion.isCancelled) return
                    val cause = error.cause
                    if (cause is ErrnoException) {
                        ctx.errnoCode(cause.errno)
                        completion.complete(Unit)
                    } else completion.completeExceptionally(error)
                }
            })
        completion.get()
    }

    override fun lookup(ctx: ExchangeContext, network: String, domain: String) {
        error("DNS транспорт использует исходные DNS-пакеты")
    }
}
