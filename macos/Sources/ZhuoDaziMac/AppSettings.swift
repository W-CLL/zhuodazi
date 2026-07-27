import Foundation

struct PetDefinition: Codable, Equatable, Identifiable {
    var id = UUID().uuidString
    var name = "自定义桌宠"
    var path = ""
}

struct LibraryDefinition: Codable, Equatable, Identifiable {
    var id = "library-\(UUID().uuidString)"
    var name = "GIF 资源库"
    var path = ""
}

struct InteractionWordPackDefinition: Codable, Equatable, Identifiable {
    var id = "words-\(UUID().uuidString)"
    var name = "互动词包"
    var words: [String: [String]] = [:]

    var wordCount: Int { words.values.reduce(0) { $0 + $1.count } }
}

struct TheaterSceneDefinition: Codable, Equatable {
    var main = ""
    var companion = ""
}

struct TheaterScriptDefinition: Codable, Equatable, Identifiable {
    var id = "theater-\(UUID().uuidString)"
    var name = "小剧场剧本"
    var scenes: [TheaterSceneDefinition] = []
}

struct ReminderDefinition: Codable, Equatable, Identifiable {
    var id = "reminder-\(UUID().uuidString)"
    var enabled = true
    var at = Date().addingTimeInterval(600)
    var message = "休息一下吧"
    var emotion = "happy"
    var repeatDaily = false
}

struct AppSettings: Codable {
    var pets: [PetDefinition] = []
    var activePetId: String?
    var size = 220
    var opacity = 100
    var alwaysOnTop = true
    var startAtLogin = false
    var clickThrough = false
    var mirrored = false
    var personality = "lively"
    var mouseInteractionEnabled = true
    var randomMovementEnabled = true
    var theaterEnabled = false
    var theaterIntervalSeconds = 300
    var theaterScripts: [TheaterScriptDefinition] = []
    var libraries: [LibraryDefinition] = []
    var activeLibraryId: String?
    var randomPetEnabled = true
    var randomPetIntervalSeconds = 300
    var interactionWordPacks: [InteractionWordPackDefinition] = []
    var activeInteractionWordPackId: String?
    var autoCheckUpdates = true
    var ignoredUpdateVersion: String?
    var reminders: [ReminderDefinition] = []
    var dockIconVisible = true
    var positionX: CGFloat?
    var positionY: CGFloat?

    init() {}

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        pets = try values.decodeIfPresent([PetDefinition].self, forKey: .pets) ?? []
        activePetId = try values.decodeIfPresent(String.self, forKey: .activePetId)
        size = try values.decodeIfPresent(Int.self, forKey: .size) ?? 220
        opacity = try values.decodeIfPresent(Int.self, forKey: .opacity) ?? 100
        alwaysOnTop = try values.decodeIfPresent(Bool.self, forKey: .alwaysOnTop) ?? true
        startAtLogin = try values.decodeIfPresent(Bool.self, forKey: .startAtLogin) ?? false
        clickThrough = try values.decodeIfPresent(Bool.self, forKey: .clickThrough) ?? false
        mirrored = try values.decodeIfPresent(Bool.self, forKey: .mirrored) ?? false
        personality = try values.decodeIfPresent(String.self, forKey: .personality) ?? "lively"
        mouseInteractionEnabled = try values.decodeIfPresent(Bool.self, forKey: .mouseInteractionEnabled) ?? true
        randomMovementEnabled = try values.decodeIfPresent(Bool.self, forKey: .randomMovementEnabled) ?? true
        theaterEnabled = try values.decodeIfPresent(Bool.self, forKey: .theaterEnabled) ?? false
        theaterIntervalSeconds = try values.decodeIfPresent(Int.self, forKey: .theaterIntervalSeconds) ?? 300
        theaterScripts = try values.decodeIfPresent([TheaterScriptDefinition].self, forKey: .theaterScripts) ?? []
        libraries = try values.decodeIfPresent([LibraryDefinition].self, forKey: .libraries) ?? []
        activeLibraryId = try values.decodeIfPresent(String.self, forKey: .activeLibraryId)
        randomPetEnabled = try values.decodeIfPresent(Bool.self, forKey: .randomPetEnabled) ?? true
        randomPetIntervalSeconds = try values.decodeIfPresent(Int.self, forKey: .randomPetIntervalSeconds) ?? 300
        interactionWordPacks = try values.decodeIfPresent([InteractionWordPackDefinition].self, forKey: .interactionWordPacks) ?? []
        activeInteractionWordPackId = try values.decodeIfPresent(String.self, forKey: .activeInteractionWordPackId)
        autoCheckUpdates = try values.decodeIfPresent(Bool.self, forKey: .autoCheckUpdates) ?? true
        ignoredUpdateVersion = try values.decodeIfPresent(String.self, forKey: .ignoredUpdateVersion)
        reminders = try values.decodeIfPresent([ReminderDefinition].self, forKey: .reminders) ?? []
        dockIconVisible = try values.decodeIfPresent(Bool.self, forKey: .dockIconVisible) ?? true
        positionX = try values.decodeIfPresent(CGFloat.self, forKey: .positionX)
        positionY = try values.decodeIfPresent(CGFloat.self, forKey: .positionY)
        normalize(resetClickThrough: true)
    }

    mutating func normalize(resetClickThrough: Bool = false) {
        pets = Array(pets.filter { !$0.id.isEmpty && !$0.path.isEmpty && FileManager.default.fileExists(atPath: $0.path) }.prefix(3))
        libraries = Array(libraries.filter { !$0.id.isEmpty && !$0.path.isEmpty }.uniqued(on: { $0.path.standardizedPath }).prefix(3))
        interactionWordPacks = Array(interactionWordPacks.filter { !$0.words.isEmpty }.prefix(5))
        theaterScripts = Array(theaterScripts.compactMap { script in
            var clean = script
            clean.scenes = Array(script.scenes.compactMap { scene in
                let main = scene.main.cleaned(limit: 60)
                let companion = scene.companion.cleaned(limit: 60)
                return main.isEmpty || companion.isEmpty ? nil : TheaterSceneDefinition(main: main, companion: companion)
            }.prefix(5))
            return clean.scenes.count >= 3 ? clean : nil
        }.prefix(10))
        reminders = Array(reminders.prefix(20))
        size = min(300, max(140, size))
        opacity = min(100, max(20, opacity))
        if !["lively", "shy", "clingy", "chaotic"].contains(personality) { personality = "lively" }
        if ![60, 180, 300, 600, 1800].contains(theaterIntervalSeconds) { theaterIntervalSeconds = 300 }
        if ![30, 60, 300, 600, 1800].contains(randomPetIntervalSeconds) { randomPetIntervalSeconds = 300 }
        if !pets.contains(where: { $0.id == activePetId }) { activePetId = nil }
        if !libraries.contains(where: { $0.id == activeLibraryId }) { activeLibraryId = nil }
        if !interactionWordPacks.contains(where: { $0.id == activeInteractionWordPackId }) { activeInteractionWordPackId = nil }
        if resetClickThrough { clickThrough = false }
    }
}

final class SettingsStore {
    private let defaults: UserDefaults
    private let key = "com.zhuodazi.desktop-pet.settings.v2"

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    func load() -> AppSettings {
        guard let data = defaults.data(forKey: key),
              var settings = try? JSONDecoder().decode(AppSettings.self, from: data) else {
            return AppSettings()
        }
        settings.normalize(resetClickThrough: true)
        return settings
    }

    func save(_ settings: AppSettings) {
        var normalized = settings
        normalized.normalize()
        guard let data = try? JSONEncoder().encode(normalized) else { return }
        defaults.set(data, forKey: key)
    }
}

private extension Array {
    func uniqued<Key: Hashable>(on key: (Element) -> Key) -> [Element] {
        var seen = Set<Key>()
        return filter { seen.insert(key($0)).inserted }
    }
}

extension String {
    func cleaned(limit: Int) -> String {
        let value = split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
        return String(value.prefix(limit))
    }

    var standardizedPath: String {
        NSString(string: self).standardizingPath.lowercased()
    }
}
