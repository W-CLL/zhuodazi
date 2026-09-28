import Foundation

public struct DailySummaryConfig: Codable, Equatable {
    public struct Weekly: Codable, Equatable { public var enabled: Bool; public var weekday: Int; public var time: String }
    public struct Monthly: Codable, Equatable { public var enabled: Bool; public var time: String }
    public struct Template: Codable, Equatable {
        public var id: String; public var kind: String; public var enabled: Bool
        public var title: String; public var body: String
    }
    public var schemaVersion: Int
    public var enabled: Bool
    public var weekly: Weekly
    public var monthly: Monthly
    public var templates: [Template]
    public var isValid: Bool {
        schemaVersion == 1 && (0...6).contains(weekly.weekday)
            && DailyClock.time(weekly.time) != nil && DailyClock.time(monthly.time) != nil
            && templates.count <= 40 && Set(templates.map(\.id)).count == templates.count
            && templates.allSatisfy {
                $0.id.range(of: "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", options: .regularExpression) != nil
                    && ["weekly", "monthly"].contains($0.kind)
                    && DailySummary.validText($0.title, limit: 80) && DailySummary.validText($0.body, limit: 2000)
            }
    }
}

public struct DailySummaryResult {
    public var available = false
    public var status = ""
    public var title = ""
    public var body = ""
    public var templateId = ""
    public var start: Date?
    public var end: Date?
    public var nextAvailable: Date?
}

public enum DailySummary {
    public static let tokens = Set(["periodStart", "periodEnd", "interactionDays", "totalInteractions", "quizzesAnswered", "quizzesCorrect", "moodRecords", "moodDays", "moodSummary", "happyCount", "offWorkDays", "overtimeDays"])
    private static let placeholder = try! NSRegularExpression(pattern: "\\{([A-Za-z][A-Za-z0-9]*)\\}")
    public static func validText(_ value: String, limit: Int) -> Bool {
        guard !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, value.utf16.count <= limit,
              !value.contains("<"), !value.contains(">"),
              !value.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) && ![9, 10, 13].contains($0.value) }) else { return false }
        let matches = placeholder.matches(in: value, range: NSRange(value.startIndex..., in: value))
        guard matches.allSatisfy({ match in
            guard let range = Range(match.range(at: 1), in: value) else { return false }
            return tokens.contains(String(value[range]))
        }) else { return false }
        let remaining = placeholder.stringByReplacingMatches(in: value, range: NSRange(value.startIndex..., in: value), withTemplate: "")
        return !remaining.contains("{") && !remaining.contains("}")
    }
    public static func parse(_ data: Data) -> DailySummaryConfig? {
        guard let config = try? JSONDecoder().decode(DailySummaryConfig.self, from: data), config.isValid else { return nil }
        return config
    }
    public static func build(config: DailySummaryConfig?, entries: [DailyEntry], kind: String, offset: Int = 0,
                             now: Date = Date(), calendar: Calendar = .current) -> DailySummaryResult {
        var result = DailySummaryResult()
        guard ["weekly", "monthly"].contains(kind), (0...1200).contains(offset) else {
            result.status = "只能查看已结束的周总结或月总结。"; return result
        }
        guard let config, config.isValid else {
            result.status = "日常总结尚未在后台配置，请联网同步后再来看看。"; return result
        }
        let label = kind == "weekly" ? "周总结" : "月总结"
        guard config.enabled, kind == "weekly" ? config.weekly.enabled : config.monthly.enabled else {
            result.status = "\(label)已在后台关闭。"; return result
        }
        let start: Date, end: Date, index: Int
        if kind == "weekly" {
            let days = (calendar.component(.weekday, from: now) - 1 - config.weekly.weekday + 7) % 7
            guard let day = calendar.date(byAdding: .day, value: -days, to: now) else { return result }
            var latest = DailyClock.atTime(config.weekly.time, on: day, calendar: calendar)
            if latest > now { latest = calendar.date(byAdding: .day, value: -7, to: latest)! }
            result.nextAvailable = calendar.date(byAdding: .day, value: 7, to: latest)
            end = calendar.date(byAdding: .day, value: -offset * 7, to: latest)!
            start = calendar.date(byAdding: .day, value: -7, to: end)!
            // Count calendar days on a fixed zone so DST cannot change rotation.
            var rotationCalendar = Calendar(identifier: .gregorian)
            rotationCalendar.timeZone = TimeZone(secondsFromGMT: 0)!
            let civil = calendar.dateComponents([.year, .month, .day], from: end)
            let date = rotationCalendar.date(from: civil)!
            index = (Int(date.timeIntervalSince1970 / 86400) + 719162) / 7
        } else {
            let month = calendar.date(from: calendar.dateComponents([.year, .month], from: now))!
            let release = DailyClock.atTime(config.monthly.time, on: month, calendar: calendar)
            let latest = now < release ? calendar.date(byAdding: .month, value: -1, to: month)! : month
            let nextMonth = calendar.date(byAdding: .month, value: 1, to: month)!
            result.nextAvailable = now < release ? release : DailyClock.atTime(config.monthly.time, on: nextMonth, calendar: calendar)
            end = calendar.date(byAdding: .month, value: -offset, to: latest)!
            start = calendar.date(byAdding: .month, value: -1, to: end)!
            index = calendar.component(.year, from: start) * 12 + calendar.component(.month, from: start) - 1
        }
        result.start = start; result.end = end
        let templates = config.templates.filter { $0.enabled && $0.kind == kind }.sorted { $0.id < $1.id }
        guard !templates.isEmpty else { result.status = "后台尚未添加可用的\(label)模板。"; return result }
        let startStamp = DailyClock.stamp(start, calendar: calendar), endStamp = DailyClock.stamp(end, calendar: calendar)
        let selected = entries.filter { $0.isValid && $0.localStamp >= startStamp && $0.localStamp < endStamp }
        guard !selected.isEmpty else { result.status = "这个已结束的周期还没有日常数据。"; return result }
        let stats = DailyStats(selected)
        let template = templates[((index % templates.count) + templates.count) % templates.count]
        let periodEnd = kind == "monthly" ? DailyClock.stamp(calendar.date(byAdding: .day, value: -1, to: end)!, calendar: calendar) : endStamp
        let values = [
            "periodStart": String(startStamp.prefix(kind == "weekly" ? 16 : 10)),
            "periodEnd": String(periodEnd.prefix(kind == "weekly" ? 16 : 10)),
            "interactionDays": String(stats.interactionDays), "totalInteractions": String(stats.total),
            "quizzesAnswered": String(stats.quizzes), "quizzesCorrect": String(stats.correct),
            "moodRecords": String(stats.moodCount), "moodDays": String(stats.moodDays), "moodSummary": stats.moodSummary,
            "happyCount": String(stats.moods["happy"] ?? 0), "offWorkDays": String(stats.offWorkDays("done")),
            "overtimeDays": String(stats.offWorkDays("overtime"))
        ]
        func render(_ text: String) -> String {
            var rendered = text
            for (key, value) in values { rendered = rendered.replacingOccurrences(of: "{\(key)}", with: value) }
            return rendered
        }
        result.available = true; result.status = "已生成"; result.templateId = template.id
        result.title = render(template.title); result.body = render(template.body)
        return result
    }
}
