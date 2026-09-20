import XCTest
@testable import ZhuoDaziCore

final class AttentionPolicyTests: XCTestCase {
    func testQuietEndsExactlyAtTheSavedDeadline() {
        let now = Date(timeIntervalSince1970: 1_789_718_400)
        XCTAssertFalse(AttentionPolicy.isQuiet(until: nil, at: now))
        XCTAssertTrue(AttentionPolicy.isQuiet(until: now.addingTimeInterval(1), at: now))
        XCTAssertFalse(AttentionPolicy.isQuiet(until: now, at: now))
        XCTAssertFalse(AttentionPolicy.isQuiet(until: now.addingTimeInterval(-1), at: now))
    }

    func testOverdueDailyReminderKeepsItsLocalTime() throws {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = try XCTUnwrap(TimeZone(identifier: "Asia/Shanghai"))
        let scheduled = try date(2026, 9, 15, 9, 30, calendar)
        let now = try date(2026, 9, 18, 10, 0, calendar)
        XCTAssertEqual(
            AttentionPolicy.nextDailyReminder(after: scheduled, now: now, calendar: calendar),
            try date(2026, 9, 19, 9, 30, calendar)
        )
    }

    func testLateNightReminderDoesNotSkipTodayWhenReopenedAfterMidnight() throws {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = try XCTUnwrap(TimeZone(identifier: "Asia/Shanghai"))
        XCTAssertEqual(
            AttentionPolicy.nextDailyReminder(
                after: try date(2026, 9, 17, 23, 0, calendar),
                now: try date(2026, 9, 18, 1, 0, calendar), calendar: calendar
            ),
            try date(2026, 9, 18, 23, 0, calendar)
        )
    }

    func testDailyReminderFollowsLocalClockAcrossDaylightSavingTime() throws {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = try XCTUnwrap(TimeZone(identifier: "America/Los_Angeles"))
        let scheduled = try date(2026, 3, 7, 9, 0, calendar)
        let expected = try date(2026, 3, 8, 9, 0, calendar)
        let next = AttentionPolicy.nextDailyReminder(after: scheduled, now: scheduled, calendar: calendar)
        XCTAssertEqual(next, expected)
        XCTAssertEqual(next.timeIntervalSince(scheduled), 23 * 60 * 60)
    }

    private func date(_ year: Int, _ month: Int, _ day: Int, _ hour: Int, _ minute: Int, _ calendar: Calendar) throws -> Date {
        try XCTUnwrap(calendar.date(from: DateComponents(year: year, month: month, day: day, hour: hour, minute: minute)))
    }
}
