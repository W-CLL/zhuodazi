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

struct CompanionVisit: Codable {
    let id: String
    let senderName: String
    let message: String
    let fileURL: URL
    var acknowledged = false
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
    }

    func refreshProfile() async throws -> CompanionProfile {
        let result: CompanionProfile = try await sendJSON(request(method: "GET", url: Self.companionURL))
        profile = result
        return result
    }

    func updateName(_ displayName: String) async throws -> CompanionProfile {
        guard !displayName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              displayName.unicodeScalars.count <= 12 else { throw CompanionError.server("昵称需要 1–12 字。") }
        let result: CompanionProfile = try await sendJSON(jsonRequest(
            method: "PATCH",
            url: Self.companionURL,
            body: ["displayName": displayName]
        ))
        profile = result
        return result
    }

    func pair(code: String) async throws -> CompanionProfile {
        guard licenses.isActivated else { throw CompanionError.server("私人搭子需要正式激活。体验期间可到大厅发送表情。") }
        let result: CompanionProfile = try await sendJSON(jsonRequest(
            method: "POST",
            url: Self.pairURL,
            body: ["code": code]
        ))
        profile = result
        return result
    }

    func unpair() async throws -> CompanionProfile {
        guard licenses.isActivated else { throw LicenseError.inactive }
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
        let data = try Data(contentsOf: fileURL, options: .mappedIfSafe)
        try validateGIF(data)
        guard var components = URLComponents(url: Self.hallDeliveriesURL
            .appendingPathComponent(recipientId), resolvingAgainstBaseURL: false) else {
            throw CompanionError.invalidResponse
        }
        components.queryItems = [URLQueryItem(name: "message", value: String(message.prefix(120)))]
        guard let uploadURL = components.url else { throw CompanionError.invalidResponse }
        var upload = request(method: "POST", url: uploadURL)
        upload.setValue("image/gif", forHTTPHeaderField: "Content-Type")
        upload.httpBody = data
        let result: SendResponse = try await sendJSON(upload)
        return result.recipientName
    }

    func sendCurrentGIF(_ fileURL: URL) async throws -> String {
        guard licenses.isActivated else { throw LicenseError.inactive }
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
        let directory = try prepareAccountInbox()
        var visits = try loadVisits(in: directory)
        let pending: DeliveryList
        do { pending = try await sendJSON(request(method: "GET", url: Self.deliveriesURL)) }
        catch {
            if !visits.isEmpty { return directory == accountInboxURL ? visits.filter(\.acknowledged) : [] }
            throw error
        }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        for item in pending.deliveries {
            guard directory == accountInboxURL else { return [] }
            guard !visits.contains(where: { $0.id == item.id }) else { continue }
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
            let fileURL = directory.appendingPathComponent("\(item.id).gif")
            try data.write(to: fileURL, options: .atomic)
            visits.append(CompanionVisit(id: item.id, senderName: item.senderName, message: item.message, fileURL: fileURL))
            // A durable local copy must exist before a device receipt is sent.
            try saveVisits(visits, in: directory)
        }
        for index in visits.indices where !visits[index].acknowledged {
            guard directory == accountInboxURL else { return [] }
            let acknowledgeURL = Self.deliveriesURL.appendingPathComponent(visits[index].id).appendingPathComponent("acknowledge")
            do {
                let (data, response) = try await session.data(for: request(method: "POST", url: acknowledgeURL))
                if (response as? HTTPURLResponse)?.statusCode != 404 { try check(response: response, data: data) }
                var acknowledgedVisits = visits
                acknowledgedVisits[index].acknowledged = true
                try saveVisits(acknowledgedVisits, in: directory)
                visits = acknowledgedVisits
            } catch {
                // Keep this item for the next acknowledgement attempt; do not show it twice.
            }
        }
        return directory == accountInboxURL ? visits.filter(\.acknowledged) : []
    }

    func isCurrentAccountVisit(_ visit: CompanionVisit) -> Bool {
        visit.fileURL.deletingLastPathComponent() == accountInboxURL
    }

    func completeVisit(_ visit: CompanionVisit) {
        let directory = visit.fileURL.deletingLastPathComponent()
        do {
            var visits = try loadVisits(in: directory)
            visits.removeAll { $0.id == visit.id }
            try saveVisits(visits, in: directory)
            try? FileManager.default.removeItem(at: visit.fileURL)
        } catch { /* Leave the local visit intact so a failed save cannot lose it. */ }
    }

    private var accountInboxURL: URL {
        inboxDirectory(for: licenses.interactionCacheKey)
    }

    private func inboxDirectory(for accountKey: String) -> URL {
        let key = SHA256.hash(data: Data(accountKey.utf8)).map { String(format: "%02x", $0) }.joined()
        return inboxURL.appendingPathComponent(key, isDirectory: true)
    }

    private func prepareAccountInbox() throws -> URL {
        let accountKey = licenses.interactionCacheKey
        let installationId = licenses.installationId.lowercased()
        let identityURL = inboxURL.appendingPathComponent("active-account.json")
        let previous: InboxIdentity?
        if FileManager.default.fileExists(atPath: identityURL.path) {
            previous = try JSONDecoder().decode(InboxIdentity.self, from: Data(contentsOf: identityURL))
        } else {
            previous = nil
        }
        let sameInstallation = previous?.installationId == installationId
        let destination = inboxDirectory(for: accountKey)
        if let previous, sameInstallation, !previous.hasActivated, licenses.isActivated,
           previous.accountKey == "pending-\(installationId)" {
            try migrateTrialVisits(from: inboxDirectory(for: previous.accountKey), to: destination)
        }
        let identity = InboxIdentity(
            accountKey: accountKey, installationId: installationId,
            hasActivated: licenses.isActivated || (sameInstallation && previous?.hasActivated == true)
        )
        if identity != previous {
            try FileManager.default.createDirectory(at: inboxURL, withIntermediateDirectories: true)
            try JSONEncoder().encode(identity).write(to: identityURL, options: .atomic)
        }
        return destination
    }

    private func migrateTrialVisits(from source: URL, to destination: URL) throws {
        let trialVisits = try loadVisits(in: source)
        guard !trialVisits.isEmpty else { return }
        var activatedVisits = try loadVisits(in: destination)
        try FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
        for visit in trialVisits where !activatedVisits.contains(where: { $0.id == visit.id }) {
            let file = destination.appendingPathComponent("\(visit.id).gif")
            try Data(contentsOf: visit.fileURL).write(to: file, options: .atomic)
            activatedVisits.append(CompanionVisit(
                id: visit.id, senderName: visit.senderName, message: visit.message,
                fileURL: file, acknowledged: visit.acknowledged
            ))
        }
        // Commit the destination before touching the trial queue. A restart can retry by ID.
        try saveVisits(activatedVisits, in: destination)
        try saveVisits([], in: source)
        for visit in trialVisits { try? FileManager.default.removeItem(at: visit.fileURL) }
    }

    private func loadVisits(in directory: URL) throws -> [CompanionVisit] {
        let metadata = directory.appendingPathComponent("pending.json")
        guard FileManager.default.fileExists(atPath: metadata.path) else { return [] }
        let data = try Data(contentsOf: metadata)
        let visits = try JSONDecoder().decode([CompanionVisit].self, from: data)
        return visits.filter { FileManager.default.fileExists(atPath: $0.fileURL.path) }
    }

    private func saveVisits(_ visits: [CompanionVisit], in directory: URL) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try JSONEncoder().encode(visits).write(to: directory.appendingPathComponent("pending.json"), options: .atomic)
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
    private struct InboxIdentity: Codable, Equatable {
        let accountKey: String
        let installationId: String
        let hasActivated: Bool
    }
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
