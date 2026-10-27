import java.net.URL
import java.security.MessageDigest
import java.util.Properties
import java.util.zip.ZipFile

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "ru.cebers.cehoproxy"
    compileSdk = 36

    defaultConfig {
        applicationId = "ru.cebers.cehoproxy"
        minSdk = 29
        targetSdk = 36
        versionCode = 12
        versionName = "0.1.11"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    val releaseKeys = File(System.getProperty("user.home"), ".android-keys/cehoproxy-release.properties").takeIf { it.exists() }?.let { file -> Properties().apply { file.inputStream().use(::load) } }
    if (releaseKeys != null) {
        signingConfigs.create("release") {
            storeFile = File(releaseKeys.getProperty("storeFile"))
            storePassword = releaseKeys.getProperty("storePassword")
            keyAlias = releaseKeys.getProperty("keyAlias")
            keyPassword = releaseKeys.getProperty("keyPassword")
        }
        buildTypes.getByName("release").signingConfig = signingConfigs.getByName("release")
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    packaging {
        resources.excludes += "/META-INF/{AL2.0,LGPL2.1}"
        jniLibs.useLegacyPackaging = true
    }
    sourceSets { getByName("main") { jniLibs.srcDirs("native-libs") } }
    splits {
        abi {
            isEnable = true
            reset()
            include("arm64-v8a", "armeabi-v7a", "x86_64", "x86")
            isUniversalApk = false
        }
    }
}

val xrayVersion = "v26.3.27"
val xrayZips = mapOf(
    "arm64-v8a" to ("Xray-android-arm64-v8a.zip" to "57149ffd48b629c07bf76938e73ab2729fde5910091497eab3e93d1c190f4c1b"),
    "x86_64" to ("Xray-android-amd64.zip" to "bfa1dd4cb3cd94f92af6c718ac6933cc84bd3ddd8343aa1e413162152930bee5"),
)
// Official Xray-core binary, run as a separate process for the xhttp/kcp nodes; not committed to git.
val fetchXray by tasks.registering {
    val outDir = layout.projectDirectory.dir("native-libs").asFile
    outputs.dir(outDir)
    doLast {
        xrayZips.forEach { (abi, spec) ->
            val (zipName, sha256) = spec
            val target = File(outDir, "$abi/libxray.so")
            if (target.exists()) return@forEach
            val zip = File(temporaryDir, zipName)
            URL("https://github.com/XTLS/Xray-core/releases/download/$xrayVersion/$zipName").openStream().use { input ->
                zip.outputStream().use { input.copyTo(it) }
            }
            val actual = MessageDigest.getInstance("SHA-256").digest(zip.readBytes()).joinToString("") { "%02x".format(it) }
            check(actual == sha256) { "SHA-256 не совпадает для $zipName: $actual" }
            target.parentFile.mkdirs()
            ZipFile(zip).use { archive ->
                archive.getInputStream(archive.getEntry("xray")).use { input -> target.outputStream().use { input.copyTo(it) } }
            }
        }
    }
}
tasks.named("preBuild") { dependsOn(fetchXray) }

configurations.configureEach {
    resolutionStrategy.eachDependency {
        if (requested.group == "org.jetbrains.kotlin" && requested.name.startsWith("kotlin-stdlib")) useVersion("2.2.10")
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.16.0")
    implementation("org.jetbrains.kotlin:kotlin-stdlib-jdk8:2.2.10")
    implementation("androidx.activity:activity-compose:1.10.1")
    implementation(platform("androidx.compose:compose-bom:2025.05.01"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.9.1")
    implementation("androidx.work:work-runtime-ktx:2.10.2")
    implementation("com.google.mlkit:barcode-scanning:17.3.0")
    implementation("com.google.android.gms:play-services-code-scanner:16.1.0")
    implementation("com.google.zxing:core:3.5.3")
    implementation("com.github.singbox-android:libbox:1.14.0")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20250517")
    testImplementation("org.jetbrains.kotlin:kotlin-test:2.2.10")
    androidTestImplementation("androidx.test.ext:junit:1.2.1")
    androidTestImplementation("androidx.test:core:1.6.1")
    androidTestImplementation("androidx.test.espresso:espresso-core:3.7.0")
    androidTestImplementation(platform("androidx.compose:compose-bom:2025.05.01"))
    androidTestImplementation("androidx.compose.ui:ui-test-junit4")
    debugImplementation("androidx.compose.ui:ui-tooling")
    debugImplementation("androidx.compose.ui:ui-test-manifest")
}
