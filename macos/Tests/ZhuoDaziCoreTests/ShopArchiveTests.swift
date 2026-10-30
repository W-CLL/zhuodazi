import XCTest
@testable import ZhuoDaziCore

final class ShopArchiveTests: XCTestCase {
    private struct File {
        var path: String
        var data = Data("hello".utf8)
        var compressed: Data? = nil
        var attributes: UInt32 = 0x81a40000
    }
    private func zip(_ files: [File]) -> Data {
        var local = Data(), central = Data()
        func append(_ value: UInt32, bytes: Int, to data: inout Data) {
            for shift in 0..<bytes { data.append(UInt8((value >> (shift * 8)) & 255)) }
        }
        for file in files {
            let offset = local.count, name = Data(file.path.utf8), payload = file.compressed ?? file.data
            let crc = ShopArchive.crc32(file.data), method: UInt32 = file.compressed == nil ? 0 : 8
            append(0x04034b50, bytes: 4, to: &local); append(20, bytes: 2, to: &local)
            append(0x0800, bytes: 2, to: &local); append(method, bytes: 2, to: &local)
            append(0, bytes: 4, to: &local); append(crc, bytes: 4, to: &local)
            append(UInt32(payload.count), bytes: 4, to: &local); append(UInt32(file.data.count), bytes: 4, to: &local)
            append(UInt32(name.count), bytes: 2, to: &local); append(0, bytes: 2, to: &local)
            local.append(name); local.append(payload)
            append(0x02014b50, bytes: 4, to: &central); append(0x0314, bytes: 2, to: &central)
            append(20, bytes: 2, to: &central); append(0x0800, bytes: 2, to: &central)
            append(method, bytes: 2, to: &central); append(0, bytes: 4, to: &central)
            append(crc, bytes: 4, to: &central); append(UInt32(payload.count), bytes: 4, to: &central)
            append(UInt32(file.data.count), bytes: 4, to: &central); append(UInt32(name.count), bytes: 2, to: &central)
            append(0, bytes: 4, to: &central); append(0, bytes: 4, to: &central)
            append(file.attributes, bytes: 4, to: &central); append(UInt32(offset), bytes: 4, to: &central); central.append(name)
        }
        let offset = local.count
        local.append(central); append(0x06054b50, bytes: 4, to: &local); append(0, bytes: 4, to: &local)
        append(UInt32(files.count), bytes: 2, to: &local); append(UInt32(files.count), bytes: 2, to: &local)
        append(UInt32(central.count), bytes: 4, to: &local); append(UInt32(offset), bytes: 4, to: &local)
        append(0, bytes: 2, to: &local); return local
    }
    func testReadsStoredAndRawDeflateWithoutExtractingPaths() throws {
        let archive = zip([File(path: "words/hello.json"),
            File(path: "theater.json", compressed: Data([0xcb,0x48,0xcd,0xc9,0xc9,0x07,0x00]))])
        let entries = try ShopArchive.read(archive)
        XCTAssertEqual(entries.map(\.path), ["words/hello.json", "theater.json"])
        XCTAssertTrue(entries.allSatisfy { $0.data == Data("hello".utf8) })
    }
    func testRejectsTraversalLinksDuplicateCaseAndParentFiles() {
        for path in ["../x.json", "/x.json", "a\\b.json", "a//b.json", "a/./b.json", "C:/x.json", "CON.json", "a/.hidden"] {
            XCTAssertThrowsError(try ShopArchive.read(zip([File(path: path)])), path)
        }
        XCTAssertThrowsError(try ShopArchive.read(zip([File(path: "A.json"), File(path: "a.json")])))
        XCTAssertThrowsError(try ShopArchive.read(zip([File(path: "link.json", attributes: 0xa1ff0000)])))
        XCTAssertThrowsError(try ShopArchive.read(zip([File(path: "parent"), File(path: "parent/child.json")])))
    }
    func testRejectsTamperingTruncationAndExpansionLimits() {
        let original = zip([File(path: "x.json")])
        var tampered = original; tampered[30 + "x.json".utf8.count] ^= 1
        XCTAssertThrowsError(try ShopArchive.read(tampered))
        var badHeader = original; badHeader[8] = 8
        XCTAssertThrowsError(try ShopArchive.read(badHeader))
        XCTAssertThrowsError(try ShopArchive.read(original.dropLast()))
        XCTAssertThrowsError(try ShopArchive.read(original, maxFileBytes: 4))
        XCTAssertThrowsError(try ShopArchive.read(original, maxTotalBytes: 4))
        XCTAssertThrowsError(try ShopArchive.read(original, maxFiles: 0))
    }
    func testCRCAndPathNormalization() {
        XCTAssertEqual(ShopArchive.crc32(Data("123456789".utf8)), 0xcbf43926)
        XCTAssertTrue(ShopArchive.safePath("角色/开心.gif"))
        XCTAssertFalse(ShopArchive.safePath("角色/开心.gif "))
    }
}
