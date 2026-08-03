import Foundation

public struct InteractionContentItem: Codable, Equatable {
    public var id: String
    public var type: String
    public var revision: Int
    public var prompt: String
    public var answer: String
    public var explanation: String
    public var choices: [String]
    public var tags: [String]
    public var difficulty: Int
    public var locale: String

    public init(
        id: String = "",
        type: String = "",
        revision: Int = 0,
        prompt: String = "",
        answer: String = "",
        explanation: String = "",
        choices: [String] = [],
        tags: [String] = [],
        difficulty: Int = 1,
        locale: String = "zh-CN"
    ) {
        self.id = id
        self.type = type
        self.revision = revision
        self.prompt = prompt
        self.answer = answer
        self.explanation = explanation
        self.choices = choices
        self.tags = tags
        self.difficulty = difficulty
        self.locale = locale
    }

    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = try values.decodeIfPresent(String.self, forKey: .id) ?? ""
        type = try values.decodeIfPresent(String.self, forKey: .type) ?? ""
        revision = try values.decodeIfPresent(Int.self, forKey: .revision) ?? 0
        prompt = try values.decodeIfPresent(String.self, forKey: .prompt) ?? ""
        answer = try values.decodeIfPresent(String.self, forKey: .answer) ?? ""
        explanation = try values.decodeIfPresent(String.self, forKey: .explanation) ?? ""
        choices = try values.decodeIfPresent([String].self, forKey: .choices) ?? []
        tags = try values.decodeIfPresent([String].self, forKey: .tags) ?? []
        difficulty = try values.decodeIfPresent(Int.self, forKey: .difficulty) ?? 1
        locale = try values.decodeIfPresent(String.self, forKey: .locale) ?? "zh-CN"
    }
}

public struct SignedContentEnvelope: Decodable {
    public let signedPayload: String
    public let sha256: String
    public let signatureAlgorithm: String
    public let signature: String
}

public struct SignedContentPayload: Codable, Equatable {
    public let schemaVersion: Int
    public let kind: String
    public let catalogVersion: Int64
    public let catalogUpdatedAt: String
    public let items: [InteractionContentItem]
    public let disabledIds: [String]

    public init(
        schemaVersion: Int,
        kind: String,
        catalogVersion: Int64,
        catalogUpdatedAt: String,
        items: [InteractionContentItem],
        disabledIds: [String]
    ) {
        self.schemaVersion = schemaVersion
        self.kind = kind
        self.catalogVersion = catalogVersion
        self.catalogUpdatedAt = catalogUpdatedAt
        self.items = items
        self.disabledIds = disabledIds
    }
}

public struct InteractionEventRecord: Codable, Equatable {
    public var eventId: String
    public var type: String
    public var occurredAt: String
    public var mood: String?
    public var contentId: String?
    public var correct: Bool?

    public init(
        eventId: String = UUID().uuidString,
        type: String,
        occurredAt: String = InteractionTimestamp.string(from: Date()),
        mood: String? = nil,
        contentId: String? = nil,
        correct: Bool? = nil
    ) {
        self.eventId = eventId
        self.type = type
        self.occurredAt = occurredAt
        self.mood = mood
        self.contentId = contentId
        self.correct = correct
    }
}

public struct InteractionShownContent: Codable, Equatable {
    public var id: String
    public var shownAt: String

    public init(id: String, shownAt: String = InteractionTimestamp.string(from: Date())) {
        self.id = id
        self.shownAt = shownAt
    }
}

public struct InteractionCacheDocument: Codable, Equatable {
    public var version = 1
    public var catalogVersion: Int64 = 0
    public var catalogUpdatedAt = ""
    public var items: [InteractionContentItem] = []
    public var shown: [InteractionShownContent] = []
    public var pendingEvents: [InteractionEventRecord] = []
    public var nextMoodPromptAt: String?
    public var lastPromptType: String?
    public var interactionMode = "standard"
    public var promptsEnabled = true
    public var profileDirty = false

    public init() {}

    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        version = try values.decodeIfPresent(Int.self, forKey: .version) ?? 1
        catalogVersion = try values.decodeIfPresent(Int64.self, forKey: .catalogVersion) ?? 0
        catalogUpdatedAt = try values.decodeIfPresent(String.self, forKey: .catalogUpdatedAt) ?? ""
        items = try values.decodeIfPresent([InteractionContentItem].self, forKey: .items) ?? []
        shown = try values.decodeIfPresent([InteractionShownContent].self, forKey: .shown) ?? []
        pendingEvents = try values.decodeIfPresent([InteractionEventRecord].self, forKey: .pendingEvents) ?? []
        nextMoodPromptAt = try values.decodeIfPresent(String.self, forKey: .nextMoodPromptAt)
        lastPromptType = try values.decodeIfPresent(String.self, forKey: .lastPromptType)
        interactionMode = try values.decodeIfPresent(String.self, forKey: .interactionMode) ?? "standard"
        promptsEnabled = try values.decodeIfPresent(Bool.self, forKey: .promptsEnabled) ?? true
        profileDirty = try values.decodeIfPresent(Bool.self, forKey: .profileDirty) ?? false
    }
}

public enum InteractionTimestamp {
    public static func string(from date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }

    public static func date(from value: String) -> Date? {
        let fractional = ISO8601DateFormatter()
        fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = fractional.date(from: value) { return date }
        return ISO8601DateFormatter().date(from: value)
    }
}

public enum InteractionRules {
    public static let contentTypes = ["joke", "math", "trivia", "riddle", "tip", "care"]

    public static func normalizeMode(_ mode: String) -> String {
        ["quiet", "standard", "lively"].contains(mode) ? mode : "standard"
    }

    public static func scheduleBounds(for mode: String) -> ClosedRange<Int> {
        switch normalizeMode(mode) {
        case "quiet": return 90...240
        case "lively": return 20...60
        default: return 45...120
        }
    }

    public static func nextDelay(for mode: String) -> TimeInterval {
        TimeInterval(Int.random(in: scheduleBounds(for: mode)) * 60)
    }

    public static func nextMoodCooldown() -> TimeInterval {
        TimeInterval(Int.random(in: 180...360) * 60)
    }

    public static func isValidItemId(_ id: String?) -> Bool {
        guard let id else { return false }
        return id.range(of: "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", options: .regularExpression) != nil
    }

    public static func isValidItem(_ item: InteractionContentItem) -> Bool {
        guard isValidItemId(item.id), contentTypes.contains(item.type), item.revision >= 1,
              (2...500).contains(item.prompt.count), (1...500).contains(item.answer.count),
              item.explanation.count <= 1_000, (1...5).contains(item.difficulty),
              item.locale.range(of: "^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", options: .regularExpression) != nil,
              item.choices.count <= 6, item.tags.count <= 10,
              item.choices.allSatisfy({ !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && $0.count <= 200 }),
              item.tags.allSatisfy({ !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && $0.count <= 30 }) else {
            return false
        }
        return item.choices.isEmpty || (item.choices.count >= 2 && item.choices.contains(item.answer))
    }

    public static func isValidPayload(_ payload: SignedContentPayload, expectedKind: String) -> Bool {
        guard payload.schemaVersion == 1, payload.kind == expectedKind, payload.catalogVersion >= 0,
              InteractionTimestamp.date(from: payload.catalogUpdatedAt) != nil,
              payload.items.count <= 5_000, payload.disabledIds.count <= 5_000,
              payload.items.allSatisfy({ isValidItem($0) }),
              payload.disabledIds.allSatisfy({ isValidItemId($0) }) else {
            return false
        }
        return Set(payload.items.map(\.id)).count == payload.items.count
            && Set(payload.disabledIds).count == payload.disabledIds.count
    }

    public static func normalizeCache(_ source: InteractionCacheDocument, now: Date = Date()) -> InteractionCacheDocument {
        var document = source
        document.version = 1
        document.catalogVersion = max(0, document.catalogVersion)

        var bestItems: [String: InteractionContentItem] = [:]
        for item in document.items where isValidItem(item) {
            if bestItems[item.id].map({ item.revision > $0.revision }) ?? true { bestItems[item.id] = item }
        }
        document.items = bestItems.values
            .sorted { ($0.type, $0.id) < ($1.type, $1.id) }
            .prefix(5_000).map { $0 }

        var bestShown: [String: InteractionShownContent] = [:]
        for item in document.shown {
            guard isValidItemId(item.id), let date = InteractionTimestamp.date(from: item.shownAt),
                  date <= now.addingTimeInterval(600), date >= now.addingTimeInterval(-30 * 86_400) else { continue }
            if let existing = bestShown[item.id], existing.shownAt >= item.shownAt { continue }
            bestShown[item.id] = item
        }
        document.shown = bestShown.values.sorted { $0.shownAt > $1.shownAt }.prefix(500).map { $0 }

        var eventIds = Set<String>()
        document.pendingEvents = document.pendingEvents.filter { event in
            guard isValidEvent(event) else { return false }
            return eventIds.insert(event.eventId.lowercased()).inserted
        }.suffix(1_000).map { $0 }

        document.interactionMode = normalizeMode(document.interactionMode)
        let promptTypes = Set(["mood"] + contentTypes)
        if document.lastPromptType.map({ !promptTypes.contains($0) }) == true { document.lastPromptType = nil }
        if let next = document.nextMoodPromptAt, InteractionTimestamp.date(from: next) == nil {
            document.nextMoodPromptAt = nil
        }
        return document
    }

    private static func isValidEvent(_ event: InteractionEventRecord) -> Bool {
        guard UUID(uuidString: event.eventId) != nil, InteractionTimestamp.date(from: event.occurredAt) != nil else {
            return false
        }
        switch event.type {
        case "mood_response": return event.mood.map { ["happy", "okay", "low"].contains($0) } ?? false
        case "content_shown", "joke_revealed": return isValidItemId(event.contentId)
        case "quiz_answered": return isValidItemId(event.contentId) && event.correct != nil
        default: return false
        }
    }
}
