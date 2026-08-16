pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.PREFER_SETTINGS)
    repositories {
        google()
        mavenCentral()
        val storageUrl = System.getenv("FLUTTER_STORAGE_BASE_URL") ?: "https://storage.googleapis.com"
        maven("$storageUrl/download.flutter.io")
    }
}

rootProject.name = "ZhuoDaziAndroid"
include(":app")

val flutterModule = settingsDir.parentFile.resolve("mobile_ui/.android/include_flutter.groovy")
apply(from = flutterModule)
