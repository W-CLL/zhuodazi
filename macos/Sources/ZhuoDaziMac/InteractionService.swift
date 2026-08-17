import CryptoKit
import Foundation
import ZhuoDaziCore

struct InteractionProfile: Decodable {
    var mode: String
    var promptsEnabled: Bool
}

enum InteractionError: LocalizedError {
    case invalidContent(String)
    case invalidResponse
    case server(String)

    var errorDescription: String? {
        switch self {
        case .invalidContent(let message), .server(let message): return message
        case .invalidResponse: return "互动服务返回的数据无效"
        }
    }
}

@MainActor
final class InteractionService {
    private struct ProfileEnvelope: Decodable { let profile: InteractionProfile }
    private struct ProfileUpdate: Encodable { let mode: String; let promptsEnabled: Bool }
    private struct BatchRequest: Encodable { let types: [String]; let limit: Int; let excludeIds: [String] }
    private struct EventBatch: Encodable { let events: [InteractionEventRecord] }
    private struct ErrorEnvelope: Decodable { let error: String?; let message: String? }

    private static let profileURL = DeskPetApi.interactionProfile
    private static let eventsURL = DeskPetApi.interactionEvents
    private static let batchURL = DeskPetApi.contentBatch
    private static let offlinePackURL = DeskPetApi.contentOfflinePack
    private static let publicKeySPKI = DeskPetApi.signingPublicKeySPKI
    private static let maximumResponseBytes = 20 * 1024 * 1024
    private static let maximumSignedPayloadBytes = 16 * 1024 * 1024
    private static let targetCacheSize = 60
    private static let eventBatchSize = 50

    private let licenses: LicenseService
    private let session: URLSession
    private let decoder = JSONDecoder()
    private let encoder = JSONEncoder()
    private var state = InteractionCacheDocument()
    private var cacheStore: InteractionCacheStore?
    private var activeAccountKey = ""
    private var profileRevision = 0
    private var networkLocked = false
    private var networkWaiters: [CheckedContinuation<Void, Never>] = []

    init(licenses: LicenseService) {
        self.licenses = licenses
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 35
        configuration.timeoutIntervalForResource = 45
        session = URLSession(configuration: configuration)
        switchAccountIfNeeded()
    }

    deinit { session.invalidateAndCancel() }

    var cachedContentCount: Int {
        switchAccountIfNeeded()
        return state.items.count
    }

    var pendingEventCount: Int {
        switchAccountIfNeeded()
        return state.pendingEvents.count
    }

    var shouldFlush: Bool { pendingEventCount >= 10 }

    var statusSummary: String {
        switchAccountIfNeeded()
        let catalog = state.catalogVersion == 0 ? "尚未同步" : "目录 v\(state.catalogVersion)"
        return "\(catalog) · \(state.items.count) 条可用 · \(state.pendingEvents.count) 条待上传"
    }

    func isMoodPromptDue(at now: Date = Date()) -> Bool {
        switchAccountIfNeeded()
        guard let value = state.nextMoodPromptAt,
              let next = InteractionTimestamp.date(from: value) else { return true }
        return now >= next
    }

    func markMoodPrompted(at now: Date = Date()) {
        switchAccountIfNeeded()
        state.nextMoodPromptAt = InteractionTimestamp.string(
            from: now.addingTimeInterval(InteractionRules.nextMoodCooldown())
        )
        state.lastPromptType = "mood"
        save()
    }

    func takeNextContent() -> InteractionContentItem? {
        switchAccountIfNeeded()
        guard !state.items.isEmpty else { return nil }
        var candidates = state.items.filter { $0.type != state.lastPromptType }
        if candidates.isEmpty { candidates = state.items }
        guard let selected = candidates.randomElement() else { return nil }
        state.items.removeAll { $0.id == selected.id }
        state.shown.removeAll { $0.id == selected.id }
        state.shown.insert(InteractionShownContent(id: selected.id), at: 0)
        state.lastPromptType = selected.type
        addEvent(InteractionEventRecord(type: "content_shown", contentId: selected.id))
        save()
        return selected
    }

    func recordMood(_ mood: String) {
        guard ["happy", "okay", "low"].contains(mood) else { return }
        switchAccountIfNeeded()
        addEvent(InteractionEventRecord(type: "mood_response", mood: mood))
        save()
    }

    func recordJoke(contentId: String) {
        guard InteractionRules.isValidItemId(contentId) else { return }
        switchAccountIfNeeded()
        addEvent(InteractionEventRecord(type: "joke_revealed", contentId: contentId))
        save()
    }

    func recordQuiz(contentId: String, correct: Bool) {
        guard InteractionRules.isValidItemId(contentId) else { return }
        switchAccountIfNeeded()
        addEvent(InteractionEventRecord(type: "quiz_answered", contentId: contentId, correct: correct))
        save()
    }

    func markProfileDirty(mode: String, promptsEnabled: Bool) {
        switchAccountIfNeeded()
        state.interactionMode = InteractionRules.normalizeMode(mode)
        state.promptsEnabled = promptsEnabled
        state.profileDirty = true
        profileRevision += 1
        save()
    }

    func syncProfile(localMode: String, localPromptsEnabled: Bool) async throws -> InteractionProfile {
        await acquireNetwork()
        defer { releaseNetwork() }

        while true {
            switchAccountIfNeeded()
            let accountKey = activeAccountKey
            let revision = profileRevision
            let dirty = state.profileDirty
            let mode = dirty ? state.interactionMode : InteractionRules.normalizeMode(localMode)
            let promptsEnabled = dirty ? state.promptsEnabled : localPromptsEnabled
            var request = URLRequest(url: Self.profileURL)
            request.timeoutInterval = 35
            if dirty {
                request.httpMethod = "PATCH"
                request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
                request.httpBody = try encoder.encode(ProfileUpdate(mode: mode, promptsEnabled: promptsEnabled))
            }
            try authorize(&request)
            let response: ProfileEnvelope = try await send(request)
            switchAccountIfNeeded()
            guard activeAccountKey == accountKey else { continue }
            guard profileRevision == revision else { continue }
            let profile = InteractionProfile(
                mode: InteractionRules.normalizeMode(response.profile.mode),
                promptsEnabled: response.profile.promptsEnabled
            )
            state.interactionMode = profile.mode
            state.promptsEnabled = profile.promptsEnabled
            state.profileDirty = false
            save()
            return profile
        }
    }

    func refill() async throws -> Int {
        await acquireNetwork()
        defer { releaseNetwork() }
        switchAccountIfNeeded()
        let accountKey = activeAccountKey
        var totalAdded = 0

        for _ in 0..<2 {
            guard state.items.count < Self.targetCacheSize else { break }
            let exclusions = Array(Set(state.items.map(\.id) + state.shown.map(\.id))).prefix(500).map { $0 }
            var request = URLRequest(url: Self.batchURL)
            request.httpMethod = "POST"
            request.timeoutInterval = 35
            request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
            request.httpBody = try encoder.encode(BatchRequest(
                types: InteractionRules.contentTypes,
                limit: 30,
                excludeIds: exclusions
            ))
            try authorize(&request)
            let envelope: SignedContentEnvelope = try await send(request)
            let payload = try validate(envelope, expectedKind: "batch")
            switchAccountIfNeeded()
            guard activeAccountKey == accountKey else { return totalAdded }
            let added = apply(payload, replacing: false)
            totalAdded += added
            if added == 0 { break }
        }
        return totalAdded
    }

    func downloadOfflinePack() async throws -> Int {
        await acquireNetwork()
        defer { releaseNetwork() }
        switchAccountIfNeeded()
        let accountKey = activeAccountKey
        var request = URLRequest(url: Self.offlinePackURL)
        request.timeoutInterval = 45
        try authorize(&request)
        let envelope: SignedContentEnvelope = try await send(request)
        let payload = try validate(envelope, expectedKind: "offline-pack")
        switchAccountIfNeeded()
        guard activeAccountKey == accountKey else { return state.items.count }
        _ = apply(payload, replacing: true)
        return state.items.count
    }

    func flushEvents() async throws {
        await acquireNetwork()
        defer { releaseNetwork() }
        switchAccountIfNeeded()
        let accountKey = activeAccountKey

        while !state.pendingEvents.isEmpty {
            let batch = Array(state.pendingEvents.prefix(Self.eventBatchSize))
            var request = URLRequest(url: Self.eventsURL)
            request.httpMethod = "POST"
            request.timeoutInterval = 35
            request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
            request.httpBody = try encoder.encode(EventBatch(events: batch))
            try authorize(&request)
            let _: EmptyResponse = try await send(request)
            switchAccountIfNeeded()
            guard activeAccountKey == accountKey else { return }
            let sentIds = Set(batch.map { $0.eventId.lowercased() })
            state.pendingEvents.removeAll { sentIds.contains($0.eventId.lowercased()) }
            save()
        }
    }

    private func apply(_ payload: SignedContentPayload, replacing: Bool) -> Int {
        guard payload.catalogVersion >= state.catalogVersion else { return 0 }
        let disabled = Set(payload.disabledIds)
        let shown = Set(state.shown.map(\.id))
        var existing: [String: InteractionContentItem] = replacing ? [:] : Dictionary(uniqueKeysWithValues: state.items
            .filter { !disabled.contains($0.id) }.map { ($0.id, $0) })
        var added = 0
        for item in payload.items where !disabled.contains(item.id) && !shown.contains(item.id) {
            if let current = existing[item.id] {
                if item.revision > current.revision { existing[item.id] = item }
            } else {
                existing[item.id] = item
                added += 1
            }
        }
        state.items = existing.values.sorted { ($0.type, $0.id) < ($1.type, $1.id) }
        state.catalogVersion = payload.catalogVersion
        state.catalogUpdatedAt = payload.catalogUpdatedAt
        save()
        return added
    }

    private func validate(_ envelope: SignedContentEnvelope, expectedKind: String) throws -> SignedContentPayload {
        guard envelope.signatureAlgorithm.lowercased() == "ed25519" else {
            throw InteractionError.invalidContent("互动内容签名算法无效")
        }
        guard envelope.signedPayload.utf8.count <= Self.maximumSignedPayloadBytes * 2,
              let payloadData = Data(base64Encoded: envelope.signedPayload),
              !payloadData.isEmpty, payloadData.count <= Self.maximumSignedPayloadBytes,
              let signature = Data(base64Encoded: envelope.signature), signature.count == 64 else {
            throw InteractionError.invalidContent("互动内容签名格式无效")
        }
        let actualHash = SHA256.hash(data: payloadData).map { String(format: "%02x", $0) }.joined()
        guard envelope.sha256.count == 64,
              actualHash.caseInsensitiveCompare(envelope.sha256) == .orderedSame else {
            throw InteractionError.invalidContent("互动内容 SHA-256 校验失败")
        }
        guard let spki = Data(base64Encoded: Self.publicKeySPKI), spki.count >= 32,
              let publicKey = try? Curve25519.Signing.PublicKey(rawRepresentation: Data(spki.suffix(32))),
              publicKey.isValidSignature(signature, for: payloadData) else {
            throw InteractionError.invalidContent("互动内容签名验证失败")
        }
        guard let payload = try? decoder.decode(SignedContentPayload.self, from: payloadData),
              InteractionRules.isValidPayload(payload, expectedKind: expectedKind) else {
            throw InteractionError.invalidContent("互动内容目录格式无效")
        }
        return payload
    }

    private func authorize(_ request: inout URLRequest) throws {
        try licenses.authorize(&request)
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
    }

    private func send<T: Decodable>(_ request: URLRequest) async throws -> T {
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw InteractionError.server("连接互动服务超时，请稍后重试")
        }
        guard data.count <= Self.maximumResponseBytes, let http = response as? HTTPURLResponse else {
            throw InteractionError.invalidResponse
        }
        guard (200..<300).contains(http.statusCode) else {
            let fallback = [401, 403].contains(http.statusCode)
                ? "设备绑定已失效，请重新绑定"
                : "互动请求失败，请稍后重试"
            throw InteractionError.server(Self.readError(data) ?? fallback)
        }
        guard let result = try? decoder.decode(T.self, from: data) else {
            throw InteractionError.invalidResponse
        }
        return result
    }

    private func switchAccountIfNeeded() {
        let accountKey = licenses.interactionCacheKey
        guard accountKey != activeAccountKey else { return }
        activeAccountKey = accountKey
        profileRevision = 0
        guard let url = try? Self.cacheURL(for: accountKey) else {
            cacheStore = nil
            state = InteractionCacheDocument()
            return
        }
        let store = InteractionCacheStore(url: url)
        cacheStore = store
        state = store.load()
    }

    private static func cacheURL(for accountKey: String) throws -> URL {
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let directory = base.appendingPathComponent("ZhuoDazi/Interactions", isDirectory: true)
        let digest = SHA256.hash(data: Data(accountKey.utf8)).map { String(format: "%02x", $0) }.joined()
        return directory.appendingPathComponent("\(digest).json")
    }

    private func addEvent(_ event: InteractionEventRecord) {
        state.pendingEvents.append(event)
        if state.pendingEvents.count > 1_000 { state.pendingEvents.removeFirst(state.pendingEvents.count - 1_000) }
    }

    private func save() {
        state = InteractionRules.normalizeCache(state)
        try? cacheStore?.save(state)
    }

    private func acquireNetwork() async {
        if !networkLocked {
            networkLocked = true
            return
        }
        await withCheckedContinuation { networkWaiters.append($0) }
    }

    private func releaseNetwork() {
        guard !networkWaiters.isEmpty else {
            networkLocked = false
            return
        }
        networkWaiters.removeFirst().resume()
    }

    private static func readError(_ data: Data) -> String? {
        guard let response = try? JSONDecoder().decode(ErrorEnvelope.self, from: data) else { return nil }
        return [response.error, response.message].compactMap { $0 }.first { !$0.isEmpty }
    }
}

private struct EmptyResponse: Decodable {}

private struct InteractionCacheStore {
    let url: URL

    func load() -> InteractionCacheDocument {
        guard let data = try? Data(contentsOf: url),
              let document = try? JSONDecoder().decode(InteractionCacheDocument.self, from: data) else {
            return InteractionCacheDocument()
        }
        return InteractionRules.normalizeCache(document)
    }

    func save(_ document: InteractionCacheDocument) throws {
        try FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        let data = try JSONEncoder().encode(InteractionRules.normalizeCache(document))
        try data.write(to: url, options: .atomic)
    }
}
