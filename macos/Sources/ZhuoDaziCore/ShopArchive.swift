import Foundation
import ShopZipInflate

public enum ShopArchiveError: Error { case invalidArchive }

/// Strict bounded ZIP reader. Nothing is extracted before the complete central
/// directory, local headers, names, sizes and CRCs have been checked.
public enum ShopArchive {
    public struct Entry { public let path: String; public let data: Data }

    public static func safePath(_ path: String) -> Bool {
        guard !path.isEmpty, path.utf8.count <= 240, !path.contains("\\"), !path.hasPrefix("/"),
              !path.contains(":"), !path.hasSuffix("/") else { return false }
        let parts = path.split(separator: "/", omittingEmptySubsequences: false)
        return parts.allSatisfy { part in
            let text = String(part)
            let stem = text.split(separator: ".").first.map(String.init)?.uppercased() ?? ""
            return !text.isEmpty && !text.hasPrefix(".") && !text.hasSuffix(".") && !text.hasSuffix(" ")
                && !text.unicodeScalars.contains(where: { $0.value < 32 || "<>:\"|?*".unicodeScalars.contains($0) })
                && !["CON", "PRN", "AUX", "NUL"].contains(stem)
                && stem.range(of: "^(COM|LPT)[1-9]$", options: .regularExpression) == nil
        }
    }

    public static func read(_ archive: Data, maxFiles: Int = 162, maxFileBytes: Int = 8 * 1024 * 1024,
                            maxTotalBytes: Int = 64 * 1024 * 1024) throws -> [Entry] {
        let bytes = [UInt8](archive)
        func u16(_ offset: Int) throws -> Int {
            guard offset >= 0, offset + 2 <= bytes.count else { throw ShopArchiveError.invalidArchive }
            return Int(bytes[offset]) | Int(bytes[offset + 1]) << 8
        }
        func u32(_ offset: Int) throws -> Int { try u16(offset) | (u16(offset + 2) << 16) }
        func slice(_ offset: Int, _ length: Int) throws -> Data {
            guard offset >= 0, length >= 0, offset <= bytes.count, length <= bytes.count - offset else { throw ShopArchiveError.invalidArchive }
            return Data(bytes[offset..<(offset + length)])
        }
        func extras(_ offset: Int, _ length: Int) throws {
            var cursor = offset
            guard offset + length <= bytes.count else { throw ShopArchiveError.invalidArchive }
            while cursor < offset + length {
                guard cursor + 4 <= offset + length, try u16(cursor) != 1 else { throw ShopArchiveError.invalidArchive }
                cursor += 4 + (try u16(cursor + 2))
            }
            guard cursor == offset + length else { throw ShopArchiveError.invalidArchive }
        }
        guard bytes.count >= 22 else { throw ShopArchiveError.invalidArchive }
        var end: Int?
        for position in stride(from: bytes.count - 22, through: max(0, bytes.count - 65557), by: -1) {
            if try u32(position) == 0x06054b50 { end = position; break }
        }
        guard let end, try u16(end + 4) == 0, try u16(end + 6) == 0 else { throw ShopArchiveError.invalidArchive }
        let count = try u16(end + 10), centralSize = try u32(end + 12), centralStart = try u32(end + 16)
        guard count > 0, count <= maxFiles, count < 65535, try u16(end + 8) == count,
              centralStart + centralSize == end, try end + 22 + u16(end + 20) == bytes.count else { throw ShopArchiveError.invalidArchive }
        var cursor = centralStart, total = 0, names = Set<String>(), entries: [Entry] = [], spans: [Range<Int>] = []
        for _ in 0..<count {
            try Task.checkCancellation()
            guard try u32(cursor) == 0x02014b50 else { throw ShopArchiveError.invalidArchive }
            let flags = try u16(cursor + 8), method = try u16(cursor + 10), checksum = try u32(cursor + 16)
            let compressed = try u32(cursor + 20), size = try u32(cursor + 24)
            let nameLength = try u16(cursor + 28), extraLength = try u16(cursor + 30), comment = try u16(cursor + 32)
            let attributes = try u32(cursor + 38), local = try u32(cursor + 42)
            guard flags & ~0x0800 == 0, [0, 8].contains(method), try u16(cursor + 34) == 0,
                  let raw = String(data: try slice(cursor + 46, nameLength), encoding: .utf8) else { throw ShopArchiveError.invalidArchive }
            let directory = raw.hasSuffix("/"), name = directory ? String(raw.dropLast()) : raw
            let key = name.precomposedStringWithCanonicalMapping.lowercased()
            guard safePath(name), names.insert(key).inserted else { throw ShopArchiveError.invalidArchive }
            let fileType = (attributes >> 16) & 0xf000
            guard fileType == 0 || fileType == (directory ? 0x4000 : 0x8000) else { throw ShopArchiveError.invalidArchive }
            try extras(cursor + 46 + nameLength, extraLength)
            cursor += 46 + nameLength + extraLength + comment
            guard cursor <= end, local < centralStart, try u32(local) == 0x04034b50,
                  try u16(local + 6) == flags, try u16(local + 8) == method,
                  try u32(local + 14) == checksum, try u32(local + 18) == compressed,
                  try u32(local + 22) == size, try u16(local + 26) == nameLength else { throw ShopArchiveError.invalidArchive }
            let localExtra = try u16(local + 28)
            guard String(data: try slice(local + 30, nameLength), encoding: .utf8) == raw else { throw ShopArchiveError.invalidArchive }
            try extras(local + 30 + nameLength, localExtra)
            let start = local + 30 + nameLength + localExtra, stop = start + compressed
            guard stop <= centralStart, !spans.contains(where: { $0.overlaps(local..<stop) }) else { throw ShopArchiveError.invalidArchive }
            spans.append(local..<stop)
            if directory {
                guard size == 0, compressed == 0 else { throw ShopArchiveError.invalidArchive }
                continue
            }
            guard size > 0, size <= maxFileBytes, total <= maxTotalBytes - size else { throw ShopArchiveError.invalidArchive }
            total += size
            let input = try slice(start, compressed)
            var output: Data
            if method == 0 { output = input }
            else {
                output = Data(count: size)
                let valid = output.withUnsafeMutableBytes { destination in
                    input.withUnsafeBytes { source in
                        shop_inflate_raw(source.bindMemory(to: UInt8.self).baseAddress, input.count,
                            destination.bindMemory(to: UInt8.self).baseAddress, size)
                    }
                }
                guard valid == 1 else { throw ShopArchiveError.invalidArchive }
            }
            guard output.count == size, crc32(output) == UInt32(checksum) else { throw ShopArchiveError.invalidArchive }
            entries.append(Entry(path: name, data: output))
        }
        guard cursor == end else { throw ShopArchiveError.invalidArchive }
        // Reject a file masquerading as a parent directory of another entry.
        let fileNames = Set(entries.map { $0.path.precomposedStringWithCanonicalMapping.lowercased() })
        for path in names {
            let parts = path.split(separator: "/")
            if parts.count > 1 {
                for length in 1..<parts.count where fileNames.contains(parts.prefix(length).joined(separator: "/")) {
                    throw ShopArchiveError.invalidArchive
                }
            }
        }
        return entries
    }

    public static func crc32(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xffffffff
        for byte in data {
            crc ^= UInt32(byte)
            for _ in 0..<8 { crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320 : 0) }
        }
        return crc ^ 0xffffffff
    }
}
