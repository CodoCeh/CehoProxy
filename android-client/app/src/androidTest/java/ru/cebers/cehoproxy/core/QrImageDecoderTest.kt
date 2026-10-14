package ru.cebers.cehoproxy.core

import android.net.Uri
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class QrImageDecoderTest {
    @Test fun decodesBundledQrFixtureOnDevice() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val image = File(instrumentation.targetContext.cacheDir, "qr-socks-local.png")
        try {
            instrumentation.context.assets.open("qr-socks-local.png").use { input ->
                image.outputStream().use { output -> input.copyTo(output) }
            }

            val decoded = QrImageDecoder.decode(instrumentation.targetContext, Uri.fromFile(image))
            assertEquals(listOf("socks5://127.0.0.1:1080#qr-smoke"), decoded)
            assertEquals(QrImportDecision.Import(decoded.single()), QrImportPolicy.decide(decoded))
        } finally {
            image.delete()
        }
    }
}
