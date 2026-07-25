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
        .executableTarget(
            name: "ZhuoDaziMac",
            path: "Sources/ZhuoDaziMac"
        )
    ]
)
