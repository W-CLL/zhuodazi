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
        versionCode = 1
        versionName = "1.0.0"
    }

    sourceSets["main"].assets.srcDirs(
        "../../macos/Resources/Pets",
        "../../content-packs/互动词包"
    )

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }
}
