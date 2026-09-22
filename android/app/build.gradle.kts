plugins {
    id("com.android.application")
}

val prepareBundledPetAssets by tasks.registering(Sync::class) {
    from("../../assets/default-pets") {
        include("*.gif")
        into("default-pets")
    }
    into(layout.buildDirectory.dir("generated/bundledPetAssets"))
}

val prepareBundledWordAssets by tasks.registering(Sync::class) {
    from("../../content-packs/互动词包") {
        include("*.json")
        into("word-packs")
    }
    into(layout.buildDirectory.dir("generated/bundledWordAssets"))
}

dependencies {
    implementation(project(":flutter"))
    implementation("androidx.core:core:1.16.0")
    implementation("com.google.crypto.tink:tink-android:1.23.0")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20240303")
    testImplementation("org.robolectric:robolectric:4.14.1")
    testImplementation("org.mockito:mockito-core:5.14.2")
}

android {
    testOptions {
        unitTests.isIncludeAndroidResources = true
        unitTests.all {
            it.systemProperty("robolectric.dependency.repo.url", "https://repo.maven.apache.org/maven2")
        }
    }

    namespace = "com.zhuodazi.android"
    compileSdk = 36
    buildToolsVersion = "36.0.0"

    packaging {
        jniLibs {
            useLegacyPackaging = true
        }
    }

    splits {
        abi {
            isEnable = true
            reset()
            include("arm64-v8a", "armeabi-v7a")
            if (providers.gradleProperty("includeX86_64").orNull == "true") {
                include("x86_64")
            }
            isUniversalApk = false
        }
    }

    defaultConfig {
        applicationId = "com.zhuodazi.android"
        minSdk = 28
        targetSdk = 36
        versionCode = 23
        versionName = "1.4.1"
    }

    sourceSets["main"].assets.srcDirs(
        layout.buildDirectory.dir("generated/bundledPetAssets"),
        layout.buildDirectory.dir("generated/bundledWordAssets")
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
        debug {
            applicationIdSuffix = ".debug"
            versionNameSuffix = "-debug"
        }
        release {
            isMinifyEnabled = true
            signingConfig = releaseSigningConfig
        }
    }
}

tasks.named("preBuild").configure {
    dependsOn(prepareBundledPetAssets, prepareBundledWordAssets)
}
