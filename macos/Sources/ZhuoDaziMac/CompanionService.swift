import CryptoKit
import Foundation

struct CompanionPartner: Decodable {
    let displayName: String
    let pairedAt: String?
}

struct CompanionProfile: Decodable {
    let displayName: String
    let pairingCode: String
    let partner: CompanionPartner?
    let hallEnabled: Bool
    let online: Bool
}

struct CompanionHallPerson: Decodable {
    let id: String
    let displayName: String
    let online: Bool
}

struct CompanionVisit {
    let id: String
    let senderName: String
    let message: String
    let fileURL: URL
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
    private static let companionURL = DeskPetApi.companion
    private static let pairURL = DeskPetApi.companionPair
    private static let deliveriesURL = DeskPetApi.companionDeliveries
    private static let hallURL = DeskPetApi.companionHall
    private static let hallDeliveriesURL = DeskPetApi.companionHallDeliveries
    private static let maximumGIFBytes = 8 * 1024 * 1024

    private let licenses: LicenseService
    private let session: URLSession
    private let inboxURL: URL
    private(set) var profile: CompanionProfile?
    private(set) var hallPeople: [CompanionHallPerson] = []

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

    func refreshHall() async throws -> [CompanionHallPerson] {
        let result: HallResponse = try await sendJSON(request(method: "GET", url: Self.hallURL))
        hallPeople = result.people.filter(\.online)
        return hallPeople
    }

    func setHallEnabled(_ enabled: Bool) async throws -> CompanionProfile {
        let result: CompanionProfile = try await sendJSON(jsonRequest(
            method: "PATCH", url: Self.hallURL, body: ["enabled": enabled]
        ))
        profile = result
        if !enabled { hallPeople = [] }
        return result
    }

    func sendToHall(fileURL: URL, recipientId: String, message: String) async throws -> String {
        guard !recipientId.isEmpty else { throw CompanionError.server("请选择一位在线用户") }
        let values = try fileURL.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey])
        guard values.isRegularFile == true, let fileSize = values.fileSize,
              fileSize >= 10, fileSize <= Self.maximumGIFBytes else {
            throw CompanionError.invalidGIF
        }
        guard var components = URLComponents(url: Self.hallDeliveriesURL
            .appendingPathComponent(recipientId), resolvingAgainstBaseURL: false) else {
            throw CompanionError.invalidResponse
        }
        components.queryItems = [URLQueryItem(name: "message", value: String(message.prefix(120)))]
        guard let uploadURL = components.url else { throw CompanionError.invalidResponse }
        var upload = request(method: "POST", url: uploadURL)
        upload.setValue("image/gif", forHTTPHeaderField: "Content-Type")
        upload.httpBody = try Data(contentsOf: fileURL, options: .mappedIfSafe)
        let result: SendResponse = try await sendJSON(upload)
        return result.recipientName
    }

    func sendCurrentGIF(_ fileURL: URL) async throws -> String {
        let values = try fileURL.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey])
        guard values.isRegularFile == true, let fileSize = values.fileSize,
              fileSize >= 10, fileSize <= Self.maximumGIFBytes else {
            throw CompanionError.invalidGIF
        }
        let data = try Data(contentsOf: fileURL, options: .mappedIfSafe)
        try validateGIF(data)
        var upload = request(method: "POST", url: Self.deliveriesURL)
        upload.setValue("image/gif", forHTTPHeaderField: "Content-Type")
        upload.httpBody = data
        let result: SendResponse = try await sendJSON(upload)
        return result.recipientName
    }

    func playTrialVisit(category: String) async throws -> CompanionVisit {
        let sticker: TrialVisitResponse = try await sendJSON(jsonRequest(
            method: "POST",
            url: DeskPetApi.trialVisitPlay,
            body: ["category": category]
        ))
        guard let downloadURL = trialVisitFileURL(sticker.downloadPath) else {
            throw CompanionError.invalidResponse
        }
        try FileManager.default.createDirectory(at: inboxURL, withIntermediateDirectories: true)
        let (data, response) = try await session.data(for: request(method: "GET", url: downloadURL))
        try check(response: response, data: data)
        try validateGIF(data)
        let digest = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
        guard digest.caseInsensitiveCompare(sticker.sha256) == .orderedSame else {
            throw CompanionError.invalidResponse
        }
        let fileURL = inboxURL.appendingPathComponent("\(sticker.id).gif")
        try data.write(to: fileURL, options: .atomic)
        return CompanionVisit(id: sticker.id, senderName: sticker.senderName, message: "", fileURL: fileURL)
    }

    func receive() async throws -> [CompanionVisit] {
        let pending: DeliveryList = try await sendJSON(request(method: "GET", url: Self.deliveriesURL))
        guard !pending.deliveries.isEmpty else { return [] }
        try FileManager.default.createDirectory(at: inboxURL, withIntermediateDirectories: true)
        var visits: [CompanionVisit] = []
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
            visits.append(CompanionVisit(id: item.id, senderName: item.senderName, message: item.message, fileURL: fileURL))
        }
        return visits
    }

    private func request(method: String, url: URL) -> URLRequest {
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.timeoutInterval = 35
        try? licenses.authorize(&request)
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
        return request
    }

    private func jsonRequest(method: String, url: URL, body: [String: Any]) -> URLRequest {
        var request = request(method: method, url: url)
        request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONSerialization.data(withJSONObject: body)
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

    private func trialVisitFileURL(_ downloadPath: String) -> URL? {
        let prefix = "/api/trial/visit-stickers/"
        let suffix = "/file"
        guard downloadPath.hasPrefix(prefix), downloadPath.hasSuffix(suffix) else { return nil }
        let id = String(downloadPath.dropFirst(prefix.count).dropLast(suffix.count))
        guard UUID(uuidString: id) != nil else { return nil }
        guard let url = URL(string: downloadPath, relativeTo: LicenseService.serviceBaseURL)?.absoluteURL,
              url.host == DeskPetApi.host,
              url.scheme?.lowercased() == "https",
              url.path == downloadPath,
              url.query == nil,
              url.fragment == nil else {
            return nil
        }
        return url
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

    private struct SendResponse: Decodable { let recipientName: String }
    private struct TrialVisitResponse: Decodable {
        let id: String
        let senderName: String
        let sha256: String
        let downloadPath: String
    }
    private struct DeliveryList: Decodable { let deliveries: [DeliveryItem] }
    private struct DeliveryItem: Decodable {
        let id: String
        let senderName: String
        let sha256: String
        let downloadPath: String
        let message: String
    }
    private struct HallResponse: Decodable { let enabled: Bool; let people: [CompanionHallPerson] }
    private struct ErrorResponse: Decodable { let error: String }
}
