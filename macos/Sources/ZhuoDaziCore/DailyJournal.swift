import Foundation

public enum DailyMood: String, Codable, CaseIterable {
    case happy, okay, bad, cry, tired, annoyed, calm, hopeful
    public var label: String {
        switch self {
        case .happy: return "开心"
        case .okay: return "一般"
        case .bad: return "糟糕"
        case .cry: return "想哭"
        case .tired: return "疲惫"
        case .annoyed: return "烦躁"
        case .calm: return "平静"
        case .hopeful: return "有盼头"
        }
    }
    public var reply: String {
        switch self {
        case .happy: return "把这一点开心收好，我也替你开心。"
        case .okay: return "平常的一天也值得好好度过。"
        case .bad: return "今天不好过也没关系，我在这里陪着。"
        case .cry: return "想哭就缓一缓，不用急着振作。"
        case .tired: return "辛苦了，给自己一点休息的时间。"
        case .annoyed: return "先慢慢呼吸，事情可以一件一件来。"
        case .calm: return "这份平静也很好，慢慢享受吧。"
        case .hopeful: return "带着这点期待，我们一起向前走。"
        }
    }

}

public enum DailyFrequency {
    public static let presets = ["eager", "frequent", "relaxed", "legacy-quiet", "legacy-standard", "legacy-lively"]
    public static func migrated(_ mode: String) -> String { "legacy-" + InteractionRules.normalizeMode(mode) }
    public static func bounds(_ value: String) -> ClosedRange<Int> {
        switch value {
        case "eager": return 5...15
        case "relaxed": return 15...40
        case "legacy-quiet": return 60...120
        case "legacy-standard": return 30...60
        default: return 10...30
        }
    }
    public static func label(_ value: String) -> String {
        switch value {
        case "eager": return "常来聊聊（5–15 分钟）"
        case "frequent": return "自在陪伴（10–30 分钟）"
        case "relaxed": return "轻轻陪着（15–40 分钟）"
        case "legacy-quiet": return "原安静频率（60–120 分钟）"
        case "legacy-standard": return "原标准频率（30–60 分钟）"
        default: return "原活跃频率（10–30 分钟）"
        }
    }
}

public struct DailyEntry: Codable, Equatable {
    public var id: String
    public var kind: String
    public var occurredAt: Date
    /// Original local clock, retained when the device later changes time zone.
    public var localStamp: String
    public var mood: String?
    public var contentId: String?
    public var correct: Bool?
    public var scenario: String?
    public var choice: String?
    public var localDay: String { String(localStamp.prefix(10)) }

    public init(kind: String, at: Date = Date(), calendar: Calendar = .current,
                mood: String? = nil, contentId: String? = nil, correct: Bool? = nil,
                scenario: String? = nil, choice: String? = nil) {
        id = UUID().uuidString
        self.kind = kind; occurredAt = at
        localStamp = DailyClock.stamp(at, calendar: calendar)
        self.mood = mood; self.contentId = contentId; self.correct = correct
        self.scenario = scenario; self.choice = choice
    }
    public var isValid: Bool {
        guard UUID(uuidString: id) != nil, localStamp.range(of: "^\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}$", options: .regularExpression) != nil else { return false }
        switch kind {
        case "mood": return DailyMood(rawValue: mood ?? "") != nil
        case "quiz": return InteractionRules.isValidItemId(contentId) && correct != nil
        case "daily":
            return scenario == "offwork" ? ["done", "overtime", "six", "rest"].contains(choice ?? "")
                : scenario == "work" && ["smooth", "busy", "stuck"].contains(choice ?? "")
        default: return false
        }
    }
}

public enum DailyClock {
    public static func stamp(_ date: Date, calendar: Calendar = .current) -> String {
        let formatter = DateFormatter()
        formatter.calendar = calendar; formatter.timeZone = calendar.timeZone
        formatter.locale = Locale(identifier: "en_US_POSIX"); formatter.dateFormat = "yyyy-MM-dd HH:mm:ss"
        return formatter.string(from: date)
    }
    public static func time(_ value: String) -> (Int, Int)? {
        guard value.range(of: "^(?:[01]\\d|2[0-3]):[0-5]\\d$", options: .regularExpression) != nil else { return nil }
        let parts = value.split(separator: ":").compactMap { Int($0) }
        return (parts[0], parts[1])
    }
    public static func atTime(_ time: String, on day: Date, calendar: Calendar) -> Date {
        let parts = self.time(time) ?? (18, 0)
        return calendar.date(bySettingHour: parts.0, minute: parts.1, second: 0, of: day) ?? day
    }
}

public final class DailyJournal {
    public private(set) var entries: [DailyEntry] = []
    public private(set) var persistenceError: String?
    private let url: URL
    private struct Document: Codable { var version = 1; var entries: [DailyEntry] }

    public init(url: URL) {
        self.url = url
        guard FileManager.default.fileExists(atPath: url.path) else { return }
        do {
            let document = try JSONDecoder().decode(Document.self, from: Data(contentsOf: url))
            guard document.version == 1 else { persistenceError = "日常数据版本暂不支持，原文件已保留。"; return }
            var seen = Set<String>()
            entries = document.entries.filter { $0.isValid && seen.insert($0.id).inserted }.sorted { $0.occurredAt < $1.occurredAt }
        } catch { persistenceError = "日常数据读取失败，原文件已保留。" }
    }
    @discardableResult public func append(_ entry: DailyEntry) -> Bool {
        guard entry.isValid, persistenceError == nil, !entries.contains(where: { $0.id == entry.id }) else { return false }
        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            let updated = entries + [entry]
            try JSONEncoder().encode(Document(entries: updated)).write(to: url, options: .atomic)
            entries = updated
            return true
        } catch { return false }
    }
}

public struct DailyStats {
    public let entries: [DailyEntry]
    public init(_ entries: [DailyEntry]) { self.entries = entries.filter(\.isValid) }
    public var total: Int { entries.count }
    public var quizzes: Int { entries.filter { $0.kind == "quiz" }.count }
    public var correct: Int { entries.filter { $0.kind == "quiz" && $0.correct == true }.count }
    public var moods: [String: Int] {
        Dictionary(grouping: entries.filter { $0.kind == "mood" }, by: { $0.mood ?? "" }).mapValues(\.count)
    }
    public var interactionDays: Int { Set(entries.map(\.localDay)).count }
    public var moodDays: Int { Set(entries.filter { $0.kind == "mood" }.map(\.localDay)).count }
    public var moodCount: Int { moods.values.reduce(0, +) }
    public func offWorkDays(_ choice: String) -> Int {
        Set(entries.filter { $0.kind == "daily" && $0.scenario == "offwork" && $0.choice == choice }.map(\.localDay)).count
    }
    public var moodSummary: String {
        let parts = DailyMood.allCases.compactMap { mood -> String? in
            guard let count = moods[mood.rawValue], count > 0 else { return nil }
            return "\(mood.label) \(count) 次"
        }
        return parts.isEmpty ? "未记录心情" : parts.joined(separator: "、")
    }
}

public struct DailyRoutine: Codable, Equatable {
    public var enabled = false
    public var time = "17:00"
    public var weekdays = [1, 2, 3, 4, 5]
    public var stateDay = ""
    public var promptCount = 0
    public var finished = false
    public var snoozedUntil: Date?
    public var lastWorkDay = ""
    public var moodDay = ""
    public var moodPromptCount = 0
    public var nextMoodAt: Date?
    public init() {}
    public mutating func beginDay(_ now: Date, calendar: Calendar = .current) {
        let day = String(DailyClock.stamp(now, calendar: calendar).prefix(10))
        if stateDay != day { stateDay = day; promptCount = 0; finished = false; snoozedUntil = nil }
        if moodDay != day { moodDay = day; moodPromptCount = 0 }
    }
    public func isWorkday(_ now: Date, calendar: Calendar = .current) -> Bool {
        enabled && weekdays.contains(calendar.component(.weekday, from: now) - 1)
    }
    public mutating func isDue(_ now: Date, calendar: Calendar = .current) -> Bool {
        beginDay(now, calendar: calendar)
        guard (snoozedUntil != nil || isWorkday(now, calendar: calendar)), !finished, promptCount < 2 else { return false }
        let target = snoozedUntil ?? DailyClock.atTime(time, on: now, calendar: calendar)
        return (promptCount == 0 || snoozedUntil != nil) && now >= target && now.timeIntervalSince(target) <= 30 * 60
    }
    public mutating func markShown(_ now: Date, calendar: Calendar = .current) {
        beginDay(now, calendar: calendar); promptCount += 1; snoozedUntil = nil
    }
    @discardableResult public mutating func snooze(_ target: Date, now: Date, calendar: Calendar = .current) -> Bool {
        beginDay(now, calendar: calendar)
        guard promptCount < 2, target > now, calendar.isDate(target, inSameDayAs: now), !finished else { return false }
        snoozedUntil = target; return true
    }
}
