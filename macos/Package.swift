// swift-tools-version: 5.10

import PackageDescription

let package = Package(
    name: "ZhuoDaziMac",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "ZhuoDaziMac", targets: ["ZhuoDaziMac"]),
        .executable(name: "ZhuoDaziUninstallHelper", targets: ["ZhuoDaziUninstallHelper"])
    ],
    targets: [
        .target(name: "ShopZipInflate", path: "Sources/ShopZipInflate", publicHeadersPath: "include",
                linkerSettings: [.linkedLibrary("z")]),
        .target(
            name: "ZhuoDaziCore",
            dependencies: ["ShopZipInflate"],
            path: "Sources/ZhuoDaziCore"
        ),
        .executableTarget(
            name: "ZhuoDaziMac",
            dependencies: ["ZhuoDaziCore"],
            path: "Sources/ZhuoDaziMac"
        ),
        .executableTarget(
            name: "ZhuoDaziUninstallHelper",
            dependencies: ["ZhuoDaziCore"],
            path: "Sources/ZhuoDaziUninstallHelper"
        ),
        .testTarget(
            name: "ZhuoDaziCoreTests",
            dependencies: ["ZhuoDaziCore"],
            path: "Tests/ZhuoDaziCoreTests"
        )
    ]
)
