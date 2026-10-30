import Foundation
import ZhuoDaziCore

struct PetDefinition: Codable, Equatable, Identifiable {
    var id = UUID().uuidString
    var name = "自定义桌宠"
    var path = ""
}

struct LibraryDefinition: Codable, Equatable, Identifiable {
    var id = "library-\(UUID().uuidString)"
    var name = "桌宠图鉴"
    var path = ""
}

struct InteractionWordPackDefinition: Codable, Equatable, Identifiable {
    var id = "words-\(UUID().uuidString)"
    var name = "悄悄话"
    var words: [String: [String]] = [:]
    var sourceResourcePackId: String?
    var sourceResourcePackSha256: String?
    var enabled = false

    var wordCount: Int { words.values.reduce(0) { $0 + $1.count } }

    init(
        id: String = "words-\(UUID().uuidString)",
        name: String = "悄悄话",
        words: [String: [String]] = [:],
        sourceResourcePackId: String? = nil,
        sourceResourcePackSha256: String? = nil,
        enabled: Bool = false
    ) {
        self.id = id
        self.name = name
        self.words = words
        self.sourceResourcePackId = sourceResourcePackId
        self.sourceResourcePackSha256 = sourceResourcePackSha256
        self.enabled = enabled
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = try values.decodeIfPresent(String.self, forKey: .id) ?? "words-\(UUID().uuidString)"
        name = try values.decodeIfPresent(String.self, forKey: .name) ?? "悄悄话"
        words = try values.decodeIfPresent([String: [String]].self, forKey: .words) ?? [:]
        sourceResourcePackId = try values.decodeIfPresent(String.self, forKey: .sourceResourcePackId)
        sourceResourcePackSha256 = try values.decodeIfPresent(String.self, forKey: .sourceResourcePackSha256)
        enabled = try values.decodeIfPresent(Bool.self, forKey: .enabled) ?? false
    }
}

struct TheaterSceneDefinition: Codable, Equatable {
    var main = ""
    var companion = ""
}

struct TheaterScriptDefinition: Codable, Equatable, Identifiable {
    var id = "theater-\(UUID().uuidString)"
    var name = "小剧场剧本"
    var scenes: [TheaterSceneDefinition] = []
    var sourceResourcePackId: String?
    var sourceResourcePackSha256: String?
    var enabled = false

    init(
        id: String = "theater-\(UUID().uuidString)",
        name: String = "小剧场剧本",
        scenes: [TheaterSceneDefinition] = [],
        sourceResourcePackId: String? = nil,
        sourceResourcePackSha256: String? = nil,
        enabled: Bool = false
    ) {
        self.id = id
        self.name = name
        self.scenes = scenes
        self.sourceResourcePackId = sourceResourcePackId
        self.sourceResourcePackSha256 = sourceResourcePackSha256
        self.enabled = enabled
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        id = try values.decodeIfPresent(String.self, forKey: .id) ?? "theater-\(UUID().uuidString)"
        name = try values.decodeIfPresent(String.self, forKey: .name) ?? "小剧场剧本"
        scenes = try values.decodeIfPresent([TheaterSceneDefinition].self, forKey: .scenes) ?? []
        sourceResourcePackId = try values.decodeIfPresent(String.self, forKey: .sourceResourcePackId)
        sourceResourcePackSha256 = try values.decodeIfPresent(String.self, forKey: .sourceResourcePackSha256)
        enabled = try values.decodeIfPresent(Bool.self, forKey: .enabled) ?? true
    }
}

struct ReminderDefinition: Codable, Equatable, Identifiable {
    var id = "reminder-\(UUID().uuidString)"
    var enabled = true
    var at = Date().addingTimeInterval(600)
    var message = "休息一下吧"
    var emotion = "happy"
    var expressionPath: String?
    var repeatDaily = false
}

struct AppSettings: Codable {
    var pets: [PetDefinition] = []
    var activePetId: String?
    var petMarketInstallations: [PetMarketInstallation] = []
    var activeMarketProductId: String?
    var enabledMarketProductIds: [String]?
    var size = 140
    var opacity = 100
    var alwaysOnTop = true
    var startAtLogin = false
    var clickThrough = false
    var mirrored = false
    var personality = "lively"
    var mouseInteractionEnabled = true
    var randomMovementEnabled = true
    var randomInteractionsEnabled = true
    var dailySpeechEnabled = true
    var quietUntilUtc: Date?
    var interactionMode = "standard"
    var dailyFrequency = "frequent"
    var dailyRoutine = DailyRoutine()
    var dailyRoutineScope = ""
    var theaterEnabled = false
    var theaterIntervalSeconds = 300
    var theaterScripts: [TheaterScriptDefinition] = []
    var libraries: [LibraryDefinition] = []
    var activeLibraryId: String?
    var enabledLibraryIds: [String]?
    var randomPetEnabled = true
    var randomPetIntervalSeconds = 30
    var interactionWordPacks: [InteractionWordPackDefinition] = []
    var activeInteractionWordPackId: String?
    var autoCheckUpdates = true
    var ignoredUpdateVersion: String?
    var reminders: [ReminderDefinition] = []
    var pendingReminders: [ReminderDefinition] = []
    var dockIconVisible = true
    var positionX: CGFloat?
    var positionY: CGFloat?
    var onboardingHintSeen = false
    var demoVisitSeen = false
    var companionHallDefaultApplied = false
    var remoteDefaultsApplied = false
    var guide = GuideProgress()
    var guideUpgradeNoticePending = false
    var hideRecoveryHintSeen = false
    var clickThroughRecoveryHintSeen = false

    init() {}

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        pets = try values.decodeIfPresent([PetDefinition].self, forKey: .pets) ?? []
        activePetId = try values.decodeIfPresent(String.self, forKey: .activePetId)
        petMarketInstallations = try values.decodeIfPresent([PetMarketInstallation].self, forKey: .petMarketInstallations) ?? []
        activeMarketProductId = try values.decodeIfPresent(String.self, forKey: .activeMarketProductId)
        enabledMarketProductIds = try values.decodeIfPresent([String].self, forKey: .enabledMarketProductIds)
        size = try values.decodeIfPresent(Int.self, forKey: .size) ?? 140
        opacity = try values.decodeIfPresent(Int.self, forKey: .opacity) ?? 100
        alwaysOnTop = try values.decodeIfPresent(Bool.self, forKey: .alwaysOnTop) ?? true
        startAtLogin = try values.decodeIfPresent(Bool.self, forKey: .startAtLogin) ?? false
        clickThrough = try values.decodeIfPresent(Bool.self, forKey: .clickThrough) ?? false
        mirrored = try values.decodeIfPresent(Bool.self, forKey: .mirrored) ?? false
        personality = try values.decodeIfPresent(String.self, forKey: .personality) ?? "lively"
        mouseInteractionEnabled = try values.decodeIfPresent(Bool.self, forKey: .mouseInteractionEnabled) ?? true
        randomMovementEnabled = try values.decodeIfPresent(Bool.self, forKey: .randomMovementEnabled) ?? true
        randomInteractionsEnabled = try values.decodeIfPresent(Bool.self, forKey: .randomInteractionsEnabled) ?? true
        dailySpeechEnabled = try values.decodeIfPresent(Bool.self, forKey: .dailySpeechEnabled) ?? true
        quietUntilUtc = try values.decodeIfPresent(Date.self, forKey: .quietUntilUtc)
        interactionMode = try values.decodeIfPresent(String.self, forKey: .interactionMode) ?? "standard"
        dailyFrequency = try values.decodeIfPresent(String.self, forKey: .dailyFrequency) ?? DailyFrequency.migrated(interactionMode)
        dailyRoutine = try values.decodeIfPresent(DailyRoutine.self, forKey: .dailyRoutine) ?? DailyRoutine()
        dailyRoutineScope = try values.decodeIfPresent(String.self, forKey: .dailyRoutineScope) ?? ""
        theaterEnabled = try values.decodeIfPresent(Bool.self, forKey: .theaterEnabled) ?? false
        theaterIntervalSeconds = try values.decodeIfPresent(Int.self, forKey: .theaterIntervalSeconds) ?? 300
        theaterScripts = try values.decodeIfPresent([TheaterScriptDefinition].self, forKey: .theaterScripts) ?? []
        libraries = try values.decodeIfPresent([LibraryDefinition].self, forKey: .libraries) ?? []
        activeLibraryId = try values.decodeIfPresent(String.self, forKey: .activeLibraryId)
        enabledLibraryIds = try values.decodeIfPresent([String].self, forKey: .enabledLibraryIds)
        randomPetEnabled = try values.decodeIfPresent(Bool.self, forKey: .randomPetEnabled) ?? true
        randomPetIntervalSeconds = try values.decodeIfPresent(Int.self, forKey: .randomPetIntervalSeconds) ?? 30
        interactionWordPacks = try values.decodeIfPresent([InteractionWordPackDefinition].self, forKey: .interactionWordPacks) ?? []
        activeInteractionWordPackId = try values.decodeIfPresent(String.self, forKey: .activeInteractionWordPackId)
        autoCheckUpdates = try values.decodeIfPresent(Bool.self, forKey: .autoCheckUpdates) ?? true
        ignoredUpdateVersion = try values.decodeIfPresent(String.self, forKey: .ignoredUpdateVersion)
        reminders = try values.decodeIfPresent([ReminderDefinition].self, forKey: .reminders) ?? []
        pendingReminders = try values.decodeIfPresent([ReminderDefinition].self, forKey: .pendingReminders) ?? []
        dockIconVisible = try values.decodeIfPresent(Bool.self, forKey: .dockIconVisible) ?? true
        positionX = try values.decodeIfPresent(CGFloat.self, forKey: .positionX)
        positionY = try values.decodeIfPresent(CGFloat.self, forKey: .positionY)
        onboardingHintSeen = try values.decodeIfPresent(Bool.self, forKey: .onboardingHintSeen) ?? false
        demoVisitSeen = try values.decodeIfPresent(Bool.self, forKey: .demoVisitSeen) ?? false
        companionHallDefaultApplied = try values.decodeIfPresent(Bool.self, forKey: .companionHallDefaultApplied) ?? false
        remoteDefaultsApplied = try values.decodeIfPresent(Bool.self, forKey: .remoteDefaultsApplied) ?? false
        guide = try values.decodeIfPresent(GuideProgress.self, forKey: .guide) ?? .legacyUpgrade
        guideUpgradeNoticePending = try values.decodeIfPresent(Bool.self, forKey: .guideUpgradeNoticePending) ?? !values.contains(.guide)
        hideRecoveryHintSeen = try values.decodeIfPresent(Bool.self, forKey: .hideRecoveryHintSeen) ?? false
        clickThroughRecoveryHintSeen = try values.decodeIfPresent(Bool.self, forKey: .clickThroughRecoveryHintSeen) ?? false
        normalize(resetClickThrough: true)
    }

    mutating func normalize(resetClickThrough: Bool = false) {
        guide.normalize()
        pets = Array(pets.filter { !$0.id.isEmpty && !$0.path.isEmpty && FileManager.default.fileExists(atPath: $0.path) }.prefix(3))
        var seenProducts = Set<String>()
        petMarketInstallations = petMarketInstallations.compactMap { installation in
            var clean = installation
            clean.productId = clean.productId.trimmingCharacters(in: .whitespacesAndNewlines)
            clean.version = clean.version.trimmingCharacters(in: .whitespacesAndNewlines)
            clean.title = clean.title.cleaned(limit: 40)
            guard clean.directory.hasPrefix("/"), !clean.directory.hasPrefix("//") else { return nil }
            let directoryURL = URL(fileURLWithPath: clean.directory, isDirectory: true).standardizedFileURL
            clean.directory = directoryURL.path
            clean.manifestSha256 = clean.manifestSha256.lowercased()
            let productKey = clean.productId.lowercased()
            var isDirectory: ObjCBool = false
            guard PetMarketJSON.validPart(clean.productId),
                  PetMarketJSON.validPart(clean.version, maximum: 40),
                  !seenProducts.contains(productKey),
                  FileManager.default.fileExists(atPath: clean.directory, isDirectory: &isDirectory),
                  isDirectory.boolValue,
                  directoryURL.resolvingSymlinksInPath().standardizedFileURL.path == directoryURL.path,
                  PetMarketJSON.validHash(clean.manifestSha256),
                  (1...80).contains(clean.files.count) else { return nil }
            var seenFiles = Set<String>()
            guard clean.files.allSatisfy({ relativePath in
                let key = relativePath.precomposedStringWithCanonicalMapping.lowercased()
                return ShopArchive.safePath(relativePath)
                    && relativePath.lowercased().hasSuffix(".gif")
                    && seenFiles.insert(key).inserted
                    && Self.isInstalledFile(relativePath, inside: directoryURL)
            }) else { return nil }
            seenProducts.insert(productKey)
            return clean
        }
        libraries = libraries.filter { !$0.id.isEmpty && !$0.path.isEmpty }
            .uniqued(on: { $0.path.standardizedPath })
        interactionWordPacks = interactionWordPacks.compactMap { pack in
            guard !pack.words.isEmpty else { return nil }
            var clean = pack
            clean.name = clean.name.cleaned(limit: 40)
            clean.sourceResourcePackId = clean.sourceResourcePackId?.cleaned(limit: 80)
            clean.sourceResourcePackSha256 = clean.sourceResourcePackSha256?.lowercased()
            clean.words = clean.words.mapValues { lines in
                Array(lines.map { $0.cleaned(limit: 60) }.filter { !$0.isEmpty }.uniqued(on: { $0 }).prefix(50))
            }.filter { !$0.value.isEmpty }
            return clean.words.isEmpty ? nil : clean
        }
        theaterScripts = theaterScripts.compactMap { script in
            var clean = script
            clean.name = clean.name.cleaned(limit: 40)
            clean.sourceResourcePackId = clean.sourceResourcePackId?.cleaned(limit: 80)
            clean.sourceResourcePackSha256 = clean.sourceResourcePackSha256?.lowercased()
            clean.scenes = Array(script.scenes.compactMap { scene in
                let main = scene.main.cleaned(limit: 60)
                let companion = scene.companion.cleaned(limit: 60)
                return main.isEmpty || companion.isEmpty ? nil : TheaterSceneDefinition(main: main, companion: companion)
            }.prefix(5))
            return clean.scenes.count >= 3 ? clean : nil
        }
        reminders = Array(reminders.prefix(20))
        size = min(ContentSelectionPolicy.maximumPetSize, max(ContentSelectionPolicy.minimumPetSize, size))
        opacity = min(100, max(20, opacity))
        if !["lively", "shy", "clingy", "chaotic"].contains(personality) { personality = "lively" }
        if !["quiet", "standard", "lively"].contains(interactionMode) { interactionMode = "standard" }
        if !DailyFrequency.presets.contains(dailyFrequency) { dailyFrequency = "frequent" }
        if DailyClock.time(dailyRoutine.time) == nil { dailyRoutine.time = "17:00" }
        dailyRoutine.weekdays = Array(Set(dailyRoutine.weekdays.filter { (0...6).contains($0) })).sorted()
        dailyRoutine.promptCount = min(2, max(0, dailyRoutine.promptCount))
        dailyRoutine.moodPromptCount = min(2, max(0, dailyRoutine.moodPromptCount))
        if ![60, 180, 300, 600, 1800].contains(theaterIntervalSeconds) { theaterIntervalSeconds = 300 }
        if ![30, 60, 300, 600, 1800].contains(randomPetIntervalSeconds) { randomPetIntervalSeconds = 30 }
        if !pets.contains(where: { $0.id == activePetId }) { activePetId = nil }
        normalizeEnabledPetSources()
        if let activeInteractionWordPackId,
           let index = interactionWordPacks.firstIndex(where: { $0.id == activeInteractionWordPackId }) {
            interactionWordPacks[index].enabled = true
        }
        activeInteractionWordPackId = nil
        var enabledWords = 0
        for index in interactionWordPacks.indices where interactionWordPacks[index].enabled {
            if enabledWords < ContentSelectionPolicy.maximumEnabledContent { enabledWords += 1 }
            else { interactionWordPacks[index].enabled = false }
        }
        var enabledScripts = 0
        for index in theaterScripts.indices where theaterScripts[index].enabled {
            if enabledScripts < ContentSelectionPolicy.maximumEnabledContent { enabledScripts += 1 }
            else { theaterScripts[index].enabled = false }
        }
        if resetClickThrough { clickThrough = false }
    }

    private mutating func normalizeEnabledPetSources() {
        let validLibraries = Set(libraries.map(\.id))
        let validProducts = Set(petMarketInstallations.map(\.productId))
        let legacyMarkets = activeMarketProductId.map { [$0] } ?? []
        let legacyLibraries = activeLibraryId.map { [$0] } ?? []
        enabledMarketProductIds = ContentSelectionPolicy.normalizedIdentifiers(
            enabledMarketProductIds ?? legacyMarkets,
            validIdentifiers: validProducts,
            limit: ContentSelectionPolicy.maximumEnabledPetSources
        )
        let remaining = ContentSelectionPolicy.maximumEnabledPetSources - (enabledMarketProductIds?.count ?? 0)
        enabledLibraryIds = ContentSelectionPolicy.normalizedIdentifiers(
            enabledLibraryIds ?? legacyLibraries,
            validIdentifiers: validLibraries,
            limit: remaining
        )
        activeMarketProductId = enabledMarketProductIds?.first
        activeLibraryId = enabledLibraryIds?.first
    }

    private static func isInstalledFile(_ relativePath: String, inside directory: URL) -> Bool {
        let fileURL = directory.appendingPathComponent(relativePath).standardizedFileURL
        guard fileURL.resolvingSymlinksInPath().standardizedFileURL.path == fileURL.path else { return false }
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: fileURL.path),
              attributes[.type] as? FileAttributeType == .typeRegular else {
            return false
        }
        let rootComponents = directory.pathComponents
        let fileComponents = fileURL.pathComponents
        guard fileComponents.count > rootComponents.count else { return false }
        return Array(fileComponents.prefix(rootComponents.count)) == rootComponents
    }
}

final class SettingsStore {
    private let defaults: UserDefaults
    private let key = "com.zhuodazi.desktop-pet.settings.v2"

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    func load() -> AppSettings {
        guard let data = defaults.data(forKey: key) else { return AppSettings() }
        guard var settings = try? JSONDecoder().decode(AppSettings.self, from: data) else {
            var recovered = AppSettings()
            recovered.guide = .legacyUpgrade
            recovered.guideUpgradeNoticePending = true
            return recovered
        }
        if let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           object["remoteDefaultsApplied"] == nil {
            settings.remoteDefaultsApplied = true
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
