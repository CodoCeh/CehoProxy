package ru.cebers.cehoproxy

import android.app.Application
import io.nekohasekai.libbox.Libbox
import io.nekohasekai.libbox.SetupOptions
import java.util.Locale

class CehoApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        val base = filesDir.apply { mkdirs() }
        val working = getExternalFilesDir(null) ?: filesDir
        val temp = cacheDir.apply { mkdirs() }
        working.mkdirs()
        Libbox.setLocale(Locale.getDefault().toLanguageTag())
        Libbox.setup(SetupOptions().apply {
            basePath = base.path
            workingPath = working.path
            tempPath = temp.path
            debug = BuildConfig.DEBUG
            logMaxLines = 1000
            appVersion = BuildConfig.VERSION_CODE.toString()
            appMarketingVersion = BuildConfig.VERSION_NAME
        })
    }
}
