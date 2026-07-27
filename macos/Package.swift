// swift-tools-version: 5.10

import PackageDescription

let package = Package(
    name: "ZhuoDaziMac",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "ZhuoDaziMac", targets: ["ZhuoDaziMac"])
    ],
    targets: [
        .target(
            name: "ZhuoDaziCore",
            path: "Sources/ZhuoDaziCore"
        ),
        .executableTarget(
            name: "ZhuoDaziMac",
            dependencies: ["ZhuoDaziCore"],
            path: "Sources/ZhuoDaziMac"
        ),
        .testTarget(
            name: "ZhuoDaziCoreTests",
            dependencies: ["ZhuoDaziCore"],
            path: "Tests/ZhuoDaziCoreTests"
        )
    ]
)
