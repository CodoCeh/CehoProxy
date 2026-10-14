package ru.cebers.cehoproxy.core

import android.content.Context
import android.net.Uri
import com.google.android.gms.tasks.Tasks
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.TimeUnit

object QrImageDecoder {
    fun decode(context: Context, uri: Uri): List<String> {
        val image = InputImage.fromFilePath(context, uri)
        val options = BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build()
        val scanner = BarcodeScanning.getClient(options)
        return try {
            Tasks.await(scanner.process(image), 20, TimeUnit.SECONDS)
                .mapNotNull { it.rawValue }
        } finally {
            scanner.close()
        }
    }
}
