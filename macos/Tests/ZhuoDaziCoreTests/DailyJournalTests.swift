import Foundation
import XCTest
@testable import ZhuoDaziCore

final class DailyJournalTests: XCTestCase {
    private var calendar: Calendar {
        var value = Calendar(identifier: .gregorian)
        value.timeZone = TimeZone(identifier: "Asia/Shanghai")!
        return value
    }
    private func date(_ value: String) -> Date { InteractionTimestamp.date(from: value + "+08:00")! }
    private func config() -> DailySummaryConfig {
        DailySummaryConfig(schemaVersion: 1, enabled: true,
            weekly: .init(enabled: true, weekday: 5, time: "18:00"), monthly: .init(enabled: true, time: "09:00"),
            templates: [
                .init(id: "week-a", kind: "weekly", enabled: true, title: "第一种", body: "{totalInteractions}/{happyCount}/{offWorkDays}/{overtimeDays}"),
                .init(id: "week-b", kind: "weekly", enabled: true, title: "第二种", body: "{periodStart}—{periodEnd} {moodSummary}"),
                .init(id: "month-a", kind: "monthly", enabled: true, title: "月度", body: "{periodStart}—{periodEnd} {quizzesAnswered}/{quizzesCorrect}")
            ])
    }
    private func mood(_ value: String, at time: String) -> DailyEntry {
        DailyEntry(kind: "mood", at: date(time), calendar: calendar, mood: value)
    }
    func testFrequencyPresetsAndMigrationDoNotMakeExistingUsersMoreFrequent() {
        XCTAssertEqual(DailyFrequency.bounds("eager"), 5...15)
        XCTAssertEqual(DailyFrequency.bounds("frequent"), 10...30)
        XCTAssertEqual(DailyFrequency.bounds("relaxed"), 15...40)
        for mode in ["quiet", "standard", "lively"] {
            XCTAssertEqual(DailyFrequency.bounds(DailyFrequency.migrated(mode)), InteractionRules.scheduleBounds(for: mode))
        }
    }
    func testEightMoodsHaveLabelsAndStayLocal() {
        XCTAssertEqual(DailyMood.allCases.count, 8)
        XCTAssertEqual(DailyMood.tired.label, "疲惫")
        XCTAssertEqual(DailyMood.hopeful.label, "有盼头")
        for mood in DailyMood.allCases {
            XCTAssertFalse(mood.label.isEmpty)
            XCTAssertFalse(mood.reply.isEmpty)
        }
    }
    func testJournalPersistsResponsesAndDeduplicatesAndScopesFiles() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: folder) }
        let url = folder.appendingPathComponent("account-a.json")
        let journal = DailyJournal(url: url)
        let entry = mood("happy", at: "2026-09-24T17:00:00")
        XCTAssertTrue(journal.append(entry))
        XCTAssertFalse(journal.append(entry))
        XCTAssertEqual(DailyJournal(url: url).entries, [entry])
        XCTAssertTrue(DailyJournal(url: folder.appendingPathComponent("account-b.json")).entries.isEmpty)
        XCTAssertFalse(journal.append(DailyEntry(kind: "content_shown")))
    }
    func testCorruptJournalIsNotOverwritten() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: url) }
        let bytes = Data("broken".utf8); try bytes.write(to: url)
        let journal = DailyJournal(url: url)
        XCTAssertNotNil(journal.persistenceError)
        XCTAssertFalse(journal.append(mood("okay", at: "2026-09-24T12:00:00")))
        XCTAssertEqual(try Data(contentsOf: url), bytes)
    }
    func testStatsCountResponsesAndDistinctWorkDays() {
        let entries = [mood("happy", at: "2026-09-24T12:00:00"), mood("cry", at: "2026-09-24T13:00:00"),
            DailyEntry(kind: "quiz", at: date("2026-09-24T14:00:00"), calendar: calendar, contentId: "math-1", correct: true),
            DailyEntry(kind: "daily", at: date("2026-09-24T18:00:00"), calendar: calendar, scenario: "offwork", choice: "done"),
            DailyEntry(kind: "daily", at: date("2026-09-24T18:01:00"), calendar: calendar, scenario: "offwork", choice: "done")]
        let stats = DailyStats(entries)
        XCTAssertEqual(stats.total, 5); XCTAssertEqual(stats.quizzes, 1); XCTAssertEqual(stats.correct, 1)
        XCTAssertEqual(stats.moodCount, 2); XCTAssertEqual(stats.moodDays, 1); XCTAssertEqual(stats.offWorkDays("done"), 1)
    }
    func testWeeklyCutoffDoesNotPublishCurrentPeriodEarly() {
        let entry = mood("happy", at: "2026-09-24T17:00:00")
        let before = DailySummary.build(config: config(), entries: [entry], kind: "weekly", now: date("2026-09-25T17:59:59"), calendar: calendar)
        XCTAssertFalse(before.available)
        XCTAssertEqual(before.end, date("2026-09-18T18:00:00"))
        let after = DailySummary.build(config: config(), entries: [entry], kind: "weekly", now: date("2026-09-25T18:00:00"), calendar: calendar)
        XCTAssertTrue(after.available)
        XCTAssertEqual(after.start, date("2026-09-18T18:00:00"))
        XCTAssertEqual(after.end, date("2026-09-25T18:00:00"))
        XCTAssertEqual(after.nextAvailable, date("2026-10-02T18:00:00"))
    }
    func testWeeklyHalfOpenIntervalIncludesWeekendAndExcludesCutoff() {
        var cfg = config(); cfg.templates = [cfg.templates[0]]
        let entries = [mood("happy", at: "2026-09-18T18:00:00"), mood("okay", at: "2026-09-20T10:00:00"),
                       mood("cry", at: "2026-09-25T18:00:00"), mood("bad", at: "2026-09-18T17:59:59")]
        let result = DailySummary.build(config: cfg, entries: entries, kind: "weekly", now: date("2026-09-25T18:00:00"), calendar: calendar)
        XCTAssertEqual(result.body, "2/1/0/0")
    }
    func testMonthlyReleaseWaitsForDayOneConfiguredHourAndLeapMonth() {
        let entry = DailyEntry(kind: "quiz", at: date("2024-02-29T23:59:00"), calendar: calendar, contentId: "test-1", correct: true)
        let before = DailySummary.build(config: config(), entries: [entry], kind: "monthly", now: date("2024-03-01T08:59:59"), calendar: calendar)
        XCTAssertFalse(before.available); XCTAssertEqual(before.end, date("2024-02-01T00:00:00"))
        let after = DailySummary.build(config: config(), entries: [entry], kind: "monthly", now: date("2024-03-01T09:00:00"), calendar: calendar)
        XCTAssertTrue(after.available)
        XCTAssertEqual(after.body, "2024-02-01—2024-02-29 1/1")
    }
    func testMonthAcrossNewYearAndFutureOffsetRejected() {
        let entry = mood("calm", at: "2025-12-31T23:00:00")
        let result = DailySummary.build(config: config(), entries: [entry], kind: "monthly", now: date("2026-01-01T09:00:00"), calendar: calendar)
        XCTAssertTrue(result.available); XCTAssertEqual(result.start, date("2025-12-01T00:00:00"))
        XCTAssertFalse(DailySummary.build(config: config(), entries: [entry], kind: "monthly", offset: -1, calendar: calendar).available)
    }
    func testTemplateRotationIsStableWithinPeriodAndChangesAcrossWeeks() {
        let entries = [mood("happy", at: "2026-09-24T12:00:00"), mood("happy", at: "2026-09-30T12:00:00")]
        let first = DailySummary.build(config: config(), entries: entries, kind: "weekly", now: date("2026-09-25T18:00:00"), calendar: calendar)
        let again = DailySummary.build(config: config(), entries: entries, kind: "weekly", now: date("2026-09-28T12:00:00"), calendar: calendar)
        let next = DailySummary.build(config: config(), entries: entries, kind: "weekly", now: date("2026-10-02T18:00:00"), calendar: calendar)
        XCTAssertEqual(first.templateId, again.templateId); XCTAssertEqual(first.body, again.body)
        XCTAssertNotEqual(first.templateId, next.templateId)
    }
    func testRemoteConfigRejectsUnknownTokensAndNoLocalProseFallback() throws {
        var cfg = config(); cfg.templates[0].body = "{unknown}"
        XCTAssertNil(DailySummary.parse(try JSONEncoder().encode(cfg)))
        XCTAssertFalse(DailySummary.validText("{happyCount", limit: 80))
        XCTAssertFalse(DailySummary.validText("<b>hello</b>", limit: 80))
        XCTAssertFalse(DailySummary.build(config: nil, entries: [mood("happy", at: "2026-09-24T12:00:00")], kind: "weekly").available)
        cfg = config(); cfg.enabled = false
        XCTAssertFalse(DailySummary.build(config: cfg, entries: [], kind: "weekly").available)
    }
    func testOriginalLocalDateIsPreservedAfterTimezoneChange() {
        let entry = mood("happy", at: "2026-09-25T17:00:00")
        XCTAssertEqual(entry.localDay, "2026-09-25")
        var other = calendar; other.timeZone = TimeZone(identifier: "America/Los_Angeles")!
        let now = ISO8601DateFormatter().date(from: "2026-09-25T18:00:00-07:00")!
        XCTAssertTrue(DailySummary.build(config: config(), entries: [entry], kind: "weekly", now: now, calendar: other).available)
    }
    func testRoutineWorkdaysDismissAndExplicitSingleSnooze() {
        var routine = DailyRoutine(); routine.enabled = true
        let first = date("2026-09-24T17:00:00")
        XCTAssertFalse(routine.isDue(date("2026-09-23T17:30:01"), calendar: calendar))
        XCTAssertFalse(routine.isDue(date("2026-09-24T16:59:59"), calendar: calendar))
        XCTAssertTrue(routine.isDue(first, calendar: calendar))
        routine.markShown(first, calendar: calendar)
        XCTAssertFalse(routine.isDue(first.addingTimeInterval(60), calendar: calendar))
        XCTAssertTrue(routine.snooze(first.addingTimeInterval(3600), now: first, calendar: calendar))
        XCTAssertFalse(routine.isDue(first.addingTimeInterval(3599), calendar: calendar))
        XCTAssertTrue(routine.isDue(first.addingTimeInterval(3600), calendar: calendar))
        routine.markShown(first.addingTimeInterval(3600), calendar: calendar)
        XCTAssertFalse(routine.snooze(first.addingTimeInterval(7200), now: first.addingTimeInterval(3600), calendar: calendar))
        XCTAssertFalse(routine.isDue(date("2026-09-26T17:00:00"), calendar: calendar))
        var manual = DailyRoutine()
        manual.markShown(first, calendar: calendar)
        XCTAssertTrue(manual.snooze(first.addingTimeInterval(1800), now: first, calendar: calendar))
        XCTAssertTrue(manual.isDue(first.addingTimeInterval(1800), calendar: calendar))
    }
    func testRoutineNextDayResetsAndNeverSnoozesAcrossMidnight() {
        var routine = DailyRoutine(); routine.enabled = true
        let first = date("2026-09-24T23:45:00")
        routine.markShown(first, calendar: calendar)
        XCTAssertFalse(routine.snooze(first.addingTimeInterval(3600), now: first, calendar: calendar))
        routine.finished = true
        XCTAssertTrue(routine.isDue(date("2026-09-25T17:00:00"), calendar: calendar))
        XCTAssertEqual(routine.promptCount, 0)
    }
    func testWeeklyCalendarIntervalsSurviveDST() {
        var dst = Calendar(identifier: .gregorian); dst.timeZone = TimeZone(identifier: "America/New_York")!
        let now = ISO8601DateFormatter().date(from: "2026-03-13T18:00:00-04:00")!
        let result = DailySummary.build(config: config(), entries: [], kind: "weekly", now: now, calendar: dst)
        XCTAssertEqual(DailyClock.stamp(result.start!, calendar: dst), "2026-03-06 18:00:00")
        XCTAssertEqual(DailyClock.stamp(result.end!, calendar: dst), "2026-03-13 18:00:00")
    }
}
