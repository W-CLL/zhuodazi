import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers
import XCTest
import ZhuoDaziCore
@testable import ZhuoDaziMac

final class PetMarketInstallerTests: XCTestCase {
    private let tinyGIF = Data(base64Encoded: "R0lGODlhAQABAIAAAP8AAAAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==")!

    private func zip(_ files: [(String, Data)]) -> Data {
        var local = Data(), central = Data()
        func append(_ value: UInt32, _ bytes: Int, to data: inout Data) {
            for shift in 0..<bytes { data.append(UInt8((UInt64(value) >> (shift * 8)) & 255)) }
        }
        for (path, payload) in files {
            let offset = local.count, name = Data(path.utf8), crc = ShopArchive.crc32(payload)
            append(0x04034b50, 4, to: &local); append(20, 2, to: &local)
            append(0x0800, 2, to: &local); append(0, 6, to: &local)
            append(crc, 4, to: &local); append(UInt32(payload.count), 4, to: &local)
            append(UInt32(payload.count), 4, to: &local); append(UInt32(name.count), 2, to: &local)
            append(0, 2, to: &local); local.append(name); local.append(payload)
            append(0x02014b50, 4, to: &central); append(0x0314, 2, to: &central)
            append(20, 2, to: &central); append(0x0800, 2, to: &central)
            append(0, 6, to: &central); append(crc, 4, to: &central)
            append(UInt32(payload.count), 4, to: &central); append(UInt32(payload.count), 4, to: &central)
            append(UInt32(name.count), 2, to: &central); append(0, 8, to: &central)
            append(0x81a40000, 4, to: &central); append(UInt32(offset), 4, to: &central)
            central.append(name)
        }
        let offset = local.count
        local.append(central); append(0x06054b50, 4, to: &local); append(0, 4, to: &local)
        append(UInt32(files.count), 2, to: &local); append(UInt32(files.count), 2, to: &local)
        append(UInt32(central.count), 4, to: &local); append(UInt32(offset), 4, to: &local)
        append(0, 2, to: &local)
        return local
    }

    private func pack(_ gifs: [(String, Data)], manifestPadding: Int = 0) throws -> (Data, PetMarketGrant) {
        var manifest = try JSONSerialization.data(withJSONObject: [
            "schemaVersion": 1, "productId": "large-pet", "version": "1", "title": "Large pet",
            "files": gifs.map { ["path": $0.0, "sizeBytes": $0.1.count, "sha256": PetMarketClient.hash($0.1)] }
        ])
        manifest.append(Data(repeating: 0x20, count: manifestPadding))
        let descriptor = PetMarketGrant(schemaVersion: 1, kind: "pet-market-download", accountId: "account",
            licenseId: "license", installationId: "installation", productId: "large-pet", version: "1",
            manifestSha256: PetMarketClient.hash(manifest), issuedAt: Date(), expiresAt: Date().addingTimeInterval(600))
        return (zip([("pet-market.json", manifest)] + gifs), descriptor)
    }

    private func withRoot(_ action: (URL) throws -> Void) throws {
        // Foundation can standardize /private/var back to the /var symlink.
        // Match the real user's Library storage so the installer's ancestor
        // checks see ordinary directories rather than the system temp alias.
        let root = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Caches", isDirectory: true)
            .appendingPathComponent("pet-market-tests-\(UUID().uuidString)", isDirectory: true)
        defer {
            if FileManager.default.fileExists(atPath: root.path) {
                try? FileManager.default.removeItem(at: root)
            }
        }
        try action(root)
    }

    func testInstallsMoreThan80GIFsAndKeepsThemAfterSettingsReload() throws {
        let (bytes, grant) = try pack((0..<100).map { ("pets/\($0).gif", tinyGIF) }, manifestPadding: 256 * 1024)
        try withRoot { root in
            let installation = try PetMarketInstaller.install(bytes, descriptor: grant, title: "", root: root)
            XCTAssertTrue(PetMarketInstaller.validInstallation(installation, root: root))
            var settings = AppSettings()
            settings.petMarketInstallations = [installation]
            settings.normalize()
            let restored = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(settings))
            XCTAssertEqual(restored.petMarketInstallations.first?.files.count, 100)
            XCTAssertEqual(restored.petMarketInstallations.first?.manifestSha256, grant.manifestSha256)
        }
    }

    func testInstallsGIFsOver8MiBInAPackOver64MiBAndRevalidates() throws {
        // GIF comment extensions enlarge a genuinely decodable image without a huge pixel allocation.
        var largeGIF = Data(tinyGIF.dropLast())
        largeGIF.append(contentsOf: [0x21, 0xfe])
        let block = Data([0xff]) + Data(repeating: 0x61, count: 255)
        while largeGIF.count <= 8 * 1024 * 1024 { largeGIF.append(block) }
        largeGIF.append(contentsOf: [0x00, 0x3b])
        let (bytes, grant) = try pack((0..<8).map { ("\($0).gif", largeGIF) })
        XCTAssertGreaterThan(bytes.count, 64 * 1024 * 1024)
        try withRoot { root in
            let installation = try PetMarketInstaller.install(bytes, descriptor: grant, title: "", root: root)
            XCTAssertEqual(installation.files.count, 8)
            XCTAssertTrue(PetMarketInstaller.validInstallation(installation, root: root))
            let file = URL(fileURLWithPath: installation.directory).appendingPathComponent("0.gif")
            var tampered = largeGIF; tampered[0] ^= 1
            try tampered.write(to: file)
            XCTAssertFalse(PetMarketInstaller.validInstallation(installation, root: root))
        }
    }

    func testRejectsInvalidGIFAndManifestAbove1MiBWithoutInstalling() throws {
        for (gifs, padding) in [([("bad.gif", Data("not a GIF".utf8))], 0),
                                ([("pet.gif", tinyGIF)], PetMarketLimits.maximumManifestBytes)] {
            let (bytes, grant) = try pack(gifs, manifestPadding: padding)
            try withRoot { root in
                XCTAssertThrowsError(try PetMarketInstaller.install(bytes, descriptor: grant, title: "", root: root))
                XCTAssertFalse(FileManager.default.fileExists(atPath: root.path))
            }
        }
    }

    func testAccepts4096PixelGIFAndRejects4097Pixels() throws {
        for width in [4096, 4097] {
            let context = try XCTUnwrap(CGContext(data: nil, width: width, height: 1, bitsPerComponent: 8,
                bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(),
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
            context.setFillColor(gray: 0, alpha: 1)
            context.fill(CGRect(x: 0, y: 0, width: CGFloat(width), height: 1))
            let image = try XCTUnwrap(context.makeImage()), output = NSMutableData()
            let destination = try XCTUnwrap(CGImageDestinationCreateWithData(output, UTType.gif.identifier as CFString, 1, nil))
            CGImageDestinationAddImage(destination, image, nil)
            XCTAssertTrue(CGImageDestinationFinalize(destination))
            let (bytes, grant) = try pack([("pet.gif", output as Data)])
            try withRoot { root in
                if width == 4096 {
                    XCTAssertNoThrow(try PetMarketInstaller.install(bytes, descriptor: grant, title: "", root: root))
                } else {
                    XCTAssertThrowsError(try PetMarketInstaller.install(bytes, descriptor: grant, title: "", root: root))
                }
            }
        }
    }
}
