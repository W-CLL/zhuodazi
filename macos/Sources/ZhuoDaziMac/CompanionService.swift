import CryptoKit
import Foundation

struct CompanionPartner: Decodable {
    let displayName: String
    let pairedAt: String?
}

struct CompanionProfile: Decodable {
    let displayName: String
    let pairingCode: String
    let todaySecretSet: Bool
    let partner: CompanionPartner?
}

struct CompanionVisit {
    let id: String
    let senderName: String
    let fileURL: URL
    let secretMatch: Bool
}

struct CompanionSticker {
    let id: String
    let senderName: String
    let stickerID: String
}

struct CompanionReceiveResult {
    let visits: [CompanionVisit]
    let stickers: [CompanionSticker]
}

struct CompanionSendResult {
    let recipientName: String
    let secretMatch: Bool
}

enum CompanionError: LocalizedError {
    case server(String)
    case invalidGIF
    case invalidResponse

    var errorDescription: String? {
        switch self {
        case .server(let message): return message
        case .invalidGIF: return "只能发送不超过 8 MB、尺寸不超过 2048×2048 的 GIF"
        case .invalidResponse: return "搭子服务返回的数据无效"
        }
    }
}

@MainActor
final class CompanionService {
    private static let companionURL = LicenseService.serviceBaseURL.appendingPathComponent("api/companion")
    private static let pairURL = companionURL.appendingPathComponent("pair")
    private static let deliveriesURL = companionURL.appendingPathComponent("deliveries")
    private static let secretURL = companionURL.appendingPathComponent("secret")
    private static let stickersURL = companionURL.appendingPathComponent("stickers")
    private static let maximumGIFBytes = 8 * 1024 * 1024

    private let licenses: LicenseService
    private let session: URLSession
    private let inboxURL: URL
    private(set) var profile: CompanionProfile?

    init(licenses: LicenseService) {
        self.licenses = licenses
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 35
        configuration.timeoutIntervalForResource = 40
        session = URLSession(configuration: configuration)
        let applicationSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        inboxURL = applicationSupport.appendingPathComponent("ZhuoDazi/companion", isDirectory: true)
        try? FileManager.default.removeItem(at: inboxURL)
    }

    func refreshProfile() async throws -> CompanionProfile {
        let result: CompanionProfile = try await sendJSON(request(method: "GET", url: Self.companionURL))
        profile = result
        return result
    }

    func updateName(_ displayName: String) async throws -> CompanionProfile {
        let result: CompanionProfile = try await sendJSON(jsonRequest(
            method: "PATCH",
            url: Self.companionURL,
            body: ["displayName": displayName]
        ))
        profile = result
        return result
    }

    func pair(code: String) async throws -> CompanionProfile {
        let result: CompanionProfile = try await sendJSON(jsonRequest(
            method: "POST",
            url: Self.pairURL,
            body: ["code": code]
        ))
        profile = result
        return result
    }

    func unpair() async throws -> CompanionProfile {
        let result: CompanionProfile = try await sendJSON(request(method: "DELETE", url: Self.pairURL))
        profile = result
        return result
    }

    func sendCurrentGIF(_ fileURL: URL) async throws -> CompanionSendResult {
        let upload = try gifUpload(url: Self.deliveriesURL, fileURL: fileURL)
        let result: SendResponse = try await sendJSON(upload)
        return CompanionSendResult(recipientName: result.recipientName, secretMatch: result.secretMatch ?? false)
    }

    func setTodaySecret(_ fileURL: URL) async throws -> Bool {
        let upload = try gifUpload(url: Self.secretURL, fileURL: fileURL)
        let result: SecretResponse = try await sendJSON(upload)
        if result.set { _ = try await refreshProfile() }
        return result.set
    }

    func sendSticker(_ stickerID: String) async throws -> String {
        let result: StickerSendResponse = try await sendJSON(jsonRequest(
            method: "POST",
            url: Self.stickersURL,
            body: ["stickerId": stickerID]
        ))
        return result.recipientName
    }

    func receive() async throws -> CompanionReceiveResult {
        let pending: DeliveryList = try await sendJSON(request(method: "GET", url: Self.deliveriesURL))
        var visits: [CompanionVisit] = []
        if !pending.deliveries.isEmpty {
            try FileManager.default.createDirectory(at: inboxURL, withIntermediateDirectories: true)
        }
        for item in pending.deliveries {
            guard let downloadURL = URL(string: item.downloadPath, relativeTo: LicenseService.serviceBaseURL) else {
                throw CompanionError.invalidResponse
            }
            let (data, response) = try await session.data(for: request(method: "GET", url: downloadURL))
            try check(response: response, data: data)
            try validateGIF(data)
            let digest = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
            guard digest.caseInsensitiveCompare(item.sha256) == .orderedSame else {
                throw CompanionError.invalidResponse
            }
            let fileURL = inboxURL.appendingPathComponent("\(item.id).gif")
            try data.write(to: fileURL, options: .atomic)
            let acknowledgeURL = Self.deliveriesURL
                .appendingPathComponent(item.id)
                .appendingPathComponent("acknowledge")
            let (_, acknowledgeResponse) = try await session.data(for: request(method: "POST", url: acknowledgeURL))
            try check(response: acknowledgeResponse, data: Data())
            visits.append(CompanionVisit(
                id: item.id,
                senderName: item.senderName,
                fileURL: fileURL,
                secretMatch: item.secretMatch
            ))
        }
        var stickers: [CompanionSticker] = []
        for item in pending.stickers {
            let acknowledgeURL = Self.stickersURL
                .appendingPathComponent(item.id)
                .appendingPathComponent("acknowledge")
            let (_, acknowledgeResponse) = try await session.data(for: request(method: "POST", url: acknowledgeURL))
            try check(response: acknowledgeResponse, data: Data())
            stickers.append(CompanionSticker(id: item.id, senderName: item.senderName, stickerID: item.stickerID))
        }
        return CompanionReceiveResult(visits: visits, stickers: stickers)
    }

    private func gifUpload(url: URL, fileURL: URL) throws -> URLRequest {
        let values = try fileURL.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey])
        guard values.isRegularFile == true, let fileSize = values.fileSize,
              fileSize >= 10, fileSize <= Self.maximumGIFBytes else {
            throw CompanionError.invalidGIF
        }
        let data = try Data(contentsOf: fileURL, options: .mappedIfSafe)
        try validateGIF(data)
        var upload = request(method: "POST", url: url)
        upload.setValue("image/gif", forHTTPHeaderField: "Content-Type")
        upload.httpBody = data
        return upload
    }

    private func request(method: String, url: URL) -> URLRequest {
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.timeoutInterval = 35
        try? licenses.authorize(&request)
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
        return request
    }

    private func jsonRequest(method: String, url: URL, body: [String: String]) -> URLRequest {
        var request = request(method: method, url: url)
        request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONEncoder().encode(body)
        return request
    }

    private func sendJSON<T: Decodable>(_ request: URLRequest) async throws -> T {
        let (data, response) = try await session.data(for: request)
        try check(response: response, data: data)
        guard let value = try? JSONDecoder().decode(T.self, from: data) else {
            throw CompanionError.invalidResponse
        }
        return value
    }

    private func check(response: URLResponse, data: Data) throws {
        guard let http = response as? HTTPURLResponse else { throw CompanionError.invalidResponse }
        guard (200..<300).contains(http.statusCode) else {
            let message = (try? JSONDecoder().decode(ErrorResponse.self, from: data))?.error
                ?? "搭子服务暂时不可用"
            throw CompanionError.server(message)
        }
    }

    private func validateGIF(_ data: Data) throws {
        guard data.count >= 10, data.count <= Self.maximumGIFBytes else { throw CompanionError.invalidGIF }
        let bytes = [UInt8](data.prefix(10))
        let signature = String(bytes: bytes[0..<6], encoding: .ascii)
        let width = Int(bytes[6]) | Int(bytes[7]) << 8
        let height = Int(bytes[8]) | Int(bytes[9]) << 8
        guard signature == "GIF87a" || signature == "GIF89a",
              width > 0, height > 0, width <= 2048, height <= 2048 else {
            throw CompanionError.invalidGIF
        }
    }

    private struct SendResponse: Decodable {
        let recipientName: String
        let secretMatch: Bool?
    }
    private struct StickerSendResponse: Decodable { let recipientName: String }
    private struct SecretResponse: Decodable { let set: Bool }
    private struct DeliveryList: Decodable {
        let deliveries: [DeliveryItem]
        let stickers: [StickerItem]
    }
    private struct DeliveryItem: Decodable {
        let id: String
        let senderName: String
        let sha256: String
        let downloadPath: String
        let secretMatch: Bool
    }
    private struct StickerItem: Decodable {
        let id: String
        let senderName: String
        let stickerID: String

        private enum CodingKeys: String, CodingKey {
            case id
            case senderName
            case stickerID = "stickerId"
        }
    }
    private struct ErrorResponse: Decodable { let error: String }
}
