import Foundation
import ImageIO
import ZhuoDaziCore

enum PetMarketInstaller {
    static func prepareDirectory(_ directory: URL) throws {
        var ancestor = directory.standardizedFileURL
        while ancestor.path != "/" {
            if FileManager.default.fileExists(atPath: ancestor.path) {
                let values = try ancestor.resourceValues(forKeys: [.isSymbolicLinkKey, .isDirectoryKey])
                guard values.isSymbolicLink != true, values.isDirectory == true else { throw PetMarketError.invalid("小铺目录不能经过链接或普通文件") }
            }
            ancestor.deleteLastPathComponent()
        }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true,
            attributes: [.posixPermissions: 0o700])
    }

    static func install(_ bytes: Data, descriptor: PetMarketGrant, title: String, root: URL) throws -> PetMarketInstallation {
        let entries = try ShopArchive.read(bytes)
        guard let manifestBytes = entries.first(where: { $0.path == "pet-market.json" })?.data,
              manifestBytes.count <= 256 * 1024, PetMarketClient.hash(manifestBytes) == descriptor.manifestSha256 else {
            throw PetMarketError.invalid("桌宠清单校验失败")
        }
        let manifest = try JSONDecoder().decode(PetMarketManifest.self, from: manifestBytes)
        guard manifest.schemaVersion == 1, manifest.productId == descriptor.productId, manifest.version == descriptor.version,
              (1...80).contains(manifest.files.count), entries.count == manifest.files.count + 1 else { throw PetMarketError.invalid("桌宠清单与授权不一致") }
        let byName = Dictionary(uniqueKeysWithValues: entries.map { ($0.path, $0.data) })
        var names = Set<String>()
        for file in manifest.files {
            guard ShopArchive.safePath(file.path), file.path.lowercased().hasSuffix(".gif"), names.insert(file.path).inserted,
                  let data = byName[file.path], data.count == file.sizeBytes,
                  PetMarketClient.hash(data) == file.sha256.lowercased(), validGIF(data) else { throw PetMarketError.invalid("桌宠素材完整性校验失败") }
        }
        let parent = root.appendingPathComponent("pets/\(descriptor.productId)", isDirectory: true)
        try prepareDirectory(parent)
        let staging = parent.appendingPathComponent(".staging-\(UUID().uuidString)", isDirectory: true)
        let destination = parent.appendingPathComponent("\(descriptor.version)-\(UUID().uuidString)", isDirectory: true)
        try prepareDirectory(staging)
        var committed = false
        defer { if !committed { try? FileManager.default.removeItem(at: staging) } }
        for entry in entries {
            try Task.checkCancellation()
            let file = staging.appendingPathComponent(entry.path)
            try prepareDirectory(file.deletingLastPathComponent())
            try entry.data.write(to: file, options: .withoutOverwriting)
        }
        try FileManager.default.moveItem(at: staging, to: destination)
        committed = true
        return PetMarketInstallation(productId: descriptor.productId, title: title.isEmpty ? manifest.title : title,
            version: descriptor.version, directory: destination.path, files: manifest.files.map(\.path),
            manifestSha256: descriptor.manifestSha256, installedAt: Date(), useGrant: nil)
    }

    static func validInstallation(_ item: PetMarketInstallation, root: URL) -> Bool {
        guard PetMarketJSON.validPart(item.productId), PetMarketJSON.validPart(item.version, maximum: 40),
              PetMarketJSON.validHash(item.manifestSha256), (1...80).contains(item.files.count) else { return false }
        let directory = URL(fileURLWithPath: item.directory).standardizedFileURL
        let expectedParent = root.appendingPathComponent("pets/\(item.productId)").standardizedFileURL
        guard directory.path.hasPrefix(expectedParent.path + "/"),
              directory.resolvingSymlinksInPath().path == directory.path,
              let manifestAttributes = try? FileManager.default.attributesOfItem(atPath: directory.appendingPathComponent("pet-market.json").path),
              manifestAttributes[.type] as? FileAttributeType == .typeRegular,
              ((manifestAttributes[.size] as? NSNumber)?.intValue ?? Int.max) <= 256 * 1024,
              let manifestBytes = try? Data(contentsOf: directory.appendingPathComponent("pet-market.json")),
              manifestBytes.count <= 256 * 1024, PetMarketClient.hash(manifestBytes) == item.manifestSha256,
              let manifest = try? JSONDecoder().decode(PetMarketManifest.self, from: manifestBytes),
              manifest.schemaVersion == 1, manifest.productId == item.productId, manifest.version == item.version,
              manifest.files.map(\.path) == item.files else { return false }
        for file in manifest.files {
            guard ShopArchive.safePath(file.path), file.path.lowercased().hasSuffix(".gif"),
                  (1...8 * 1024 * 1024).contains(file.sizeBytes) else { return false }
            let url = directory.appendingPathComponent(file.path)
            guard url.resolvingSymlinksInPath().path == url.path,
                  let attributes = try? FileManager.default.attributesOfItem(atPath: url.path),
                  attributes[.type] as? FileAttributeType == .typeRegular,
                  (attributes[.size] as? NSNumber)?.intValue == file.sizeBytes,
                  let bytes = try? Data(contentsOf: url), PetMarketClient.hash(bytes) == file.sha256.lowercased() else { return false }
        }
        return true
    }

    private static func validGIF(_ data: Data) -> Bool {
        guard data.count >= 10, ["GIF87a", "GIF89a"].contains(String(data: data.prefix(6), encoding: .ascii) ?? ""),
              let source = CGImageSourceCreateWithData(data as CFData, nil), CGImageSourceGetCount(source) > 0,
              let values = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let width = values[kCGImagePropertyPixelWidth] as? Int, let height = values[kCGImagePropertyPixelHeight] as? Int,
              (1...2048).contains(width), (1...2048).contains(height) else { return false }
        return true
    }
}
