plugins {
    id("com.android.application")
}

android {
    namespace = "com.zhuodazi.android"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.zhuodazi.android"
        minSdk = 28
        targetSdk = 36
        versionCode = 2
        versionName = "1.1.0"
    }

    sourceSets["main"].assets.srcDirs(
        "../../macos/Resources/Pets",
        "../../content-packs/互动词包"
    )

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    val signingStorePath = System.getenv("ANDROID_KEYSTORE_PATH")
    val signingStorePassword = System.getenv("ANDROID_STORE_PASSWORD")
    val signingKeyAlias = System.getenv("ANDROID_KEY_ALIAS")
    val signingKeyPassword = System.getenv("ANDROID_KEY_PASSWORD")
    val releaseSigningConfig = if (listOf(
            signingStorePath,
            signingStorePassword,
            signingKeyAlias,
            signingKeyPassword
        ).all { !it.isNullOrBlank() }) {
        signingConfigs.create("zhuodaziRelease") {
            storeFile = file(signingStorePath!!)
            storePassword = signingStorePassword
            keyAlias = signingKeyAlias
            keyPassword = signingKeyPassword
        }
    } else null

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = releaseSigningConfig
        }
    }
}
