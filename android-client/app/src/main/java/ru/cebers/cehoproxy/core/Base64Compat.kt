package ru.cebers.cehoproxy.core

import java.io.ByteArrayOutputStream

/** Base64 decoder compatible with Android API 24 and ordinary JVM unit tests. */
object Base64Compat {
    fun decode(input: String): ByteArray {
        val compact = input.filterNot(Char::isWhitespace)
        val paddingAt = compact.indexOf('=')
        val payload = if (paddingAt >= 0) {
            require(compact.substring(paddingAt).all { it == '=' } && compact.length - paddingAt <= 2) { "Invalid Base64 padding" }
            compact.substring(0, paddingAt)
        } else compact
        require(payload.length % 4 != 1) { "Invalid Base64 length" }

        val output = ByteArrayOutputStream(payload.length * 3 / 4)
        var accumulator = 0
        var bitCount = 0
        for (character in payload) {
            val value = when (character) {
                in 'A'..'Z' -> character - 'A'
                in 'a'..'z' -> character - 'a' + 26
                in '0'..'9' -> character - '0' + 52
                '+', '-' -> 62
                '/', '_' -> 63
                else -> throw IllegalArgumentException("Invalid Base64 character")
            }
            accumulator = (accumulator shl 6) or value
            bitCount += 6
            if (bitCount >= 8) {
                bitCount -= 8
                output.write((accumulator shr bitCount) and 0xFF)
            }
        }
        return output.toByteArray()
    }
}
