package ru.cebers.cehoproxy.core

import android.content.Context
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

internal object EventLog {
    private const val FILE = "events.log"
    private const val MAX_LINES = 300
    private val ipv4 = Regex("""\b\d{1,3}(\.\d{1,3}){3}\b""")
    private val ipv6 = Regex("""\b([0-9a-fA-F]{1,4}:){2,7}[0-9a-fA-F]{1,4}\b""")
    private val host = Regex("""\b[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)+\b""")
    private val uuid = Regex("""\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b""")

    /** Removes addresses, hostnames and identifiers so the log can be shared. */
    fun redact(text: String): String = text
        .replace(uuid, "[id]")
        .replace(ipv6, "[ip]")
        .replace(ipv4, "[ip]")
        .replace(host, "[host]")

    fun append(context: Context, message: String, nowMillis: Long = System.currentTimeMillis()) {
        runCatching {
            val file = File(context.filesDir, FILE)
            val stamp = SimpleDateFormat("dd.MM HH:mm:ss", Locale.ROOT).format(Date(nowMillis))
            val lines = (if (file.exists()) file.readLines() else emptyList()) + "$stamp ${redact(message)}"
            file.writeText(lines.takeLast(MAX_LINES).joinToString("\n", postfix = "\n"))
        }
    }

    fun read(context: Context): String = runCatching { File(context.filesDir, FILE).takeIf { it.exists() }?.readText().orEmpty() }.getOrDefault("")
}
