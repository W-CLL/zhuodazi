import Foundation

public enum AttentionPolicy {
    public static func isQuiet(until: Date?, at now: Date = Date()) -> Bool {
        until.map { $0 > now } ?? false
    }

    public static func nextDailyReminder(after scheduled: Date, now: Date, calendar: Calendar = .current) -> Date {
        var next = scheduled
        repeat {
            next = calendar.date(byAdding: .day, value: 1, to: next) ?? next.addingTimeInterval(86_400)
        } while next <= now
        return next
    }
}
