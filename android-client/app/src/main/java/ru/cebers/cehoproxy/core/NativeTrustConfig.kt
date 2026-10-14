package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject

object NativeTrustConfig {
    fun applyAndroidSystemTrust(config: String, certificatePath: () -> String): String {
        val root = JSONObject(config)
        if (root.has("certificate")) return config
        root.put("certificate", JSONObject().put("store", "none")
            .put("certificate_path", JSONArray().put(certificatePath())))
        return root.toString(2)
    }
}
