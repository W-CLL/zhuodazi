import XCTest
@testable import ZhuoDaziCore

final class InteractionCoreTests: XCTestCase {
    func testScheduleBoundsMatchInteractionModes() {
        XCTAssertEqual(InteractionRules.scheduleBounds(for: "quiet"), 60...120)
        XCTAssertEqual(InteractionRules.scheduleBounds(for: "standard"), 30...60)
        XCTAssertEqual(InteractionRules.scheduleBounds(for: "lively"), 10...30)
        XCTAssertEqual(InteractionRules.scheduleBounds(for: "unknown"), 30...60)
    }

    func testContentValidationRequiresAnswerAmongChoices() {
        var item = validItem()
        item.choices = ["3", "4", "5"]
        item.answer = "4"
        XCTAssertTrue(InteractionRules.isValidItem(item))

        item.answer = "6"
        XCTAssertFalse(InteractionRules.isValidItem(item))
        item.id = "invalid id"
        XCTAssertFalse(InteractionRules.isValidItem(item))
    }

    func testPayloadRejectsDuplicateAndMalformedContentIds() {
        let item = validItem()
        let timestamp = InteractionTimestamp.string(from: Date())
        let duplicate = SignedContentPayload(
            schemaVersion: 1,
            kind: "batch",
            catalogVersion: 3,
            catalogUpdatedAt: timestamp,
            items: [item, item],
            disabledIds: []
        )
        XCTAssertFalse(InteractionRules.isValidPayload(duplicate, expectedKind: "batch"))

        let valid = SignedContentPayload(
            schemaVersion: 1,
            kind: "batch",
            catalogVersion: 3,
            catalogUpdatedAt: timestamp,
            items: [item],
            disabledIds: ["retired:item-2"]
        )
        XCTAssertTrue(InteractionRules.isValidPayload(valid, expectedKind: "batch"))
    }

    func testCacheMigrationDropsOfflineItemsButPreservesShownIdsEventsAndPreferences() {
        let now = Date()
        var old = validItem()
        old.revision = 1
        var current = old
        current.revision = 2
        current.explanation = "新版说明"
        var invalid = validItem()
        invalid.id = "bad id"

        var document = InteractionCacheDocument()
        document.items = [old, current, invalid]
        document.shown = [
            InteractionShownContent(id: old.id, shownAt: InteractionTimestamp.string(from: now)),
            InteractionShownContent(id: "expired", shownAt: InteractionTimestamp.string(from: now.addingTimeInterval(-31 * 86_400)))
        ]
        document.pendingEvents = [
            InteractionEventRecord(type: "content_shown", contentId: old.id),
            InteractionEventRecord(eventId: "not-a-uuid", type: "content_shown", contentId: old.id)
        ]
        document.interactionMode = "unexpected"
        document.lastPromptType = "unsupported"
        document.promptsEnabled = false
        document.profileDirty = true
        document.nextMoodPromptAt = InteractionTimestamp.string(from: now.addingTimeInterval(3600))

        let normalized = InteractionRules.normalizeCache(document, now: now)
        XCTAssertTrue(normalized.items.isEmpty)
        XCTAssertEqual(normalized.shown.map(\.id), [old.id])
        XCTAssertEqual(normalized.pendingEvents.count, 1)
        XCTAssertEqual(normalized.interactionMode, "standard")
        XCTAssertNil(normalized.lastPromptType)
        XCTAssertFalse(normalized.promptsEnabled)
        XCTAssertTrue(normalized.profileDirty)
        XCTAssertEqual(normalized.nextMoodPromptAt, document.nextMoodPromptAt)
    }

    func testRealtimeRequestEncodesOneItemAndOnlyShownExclusions() throws {
        let request = RealtimeContentRequest(quizOnly: true, shown: [
            InteractionShownContent(id: "seen-1"), InteractionShownContent(id: "seen-1"),
            InteractionShownContent(id: "invalid id"), InteractionShownContent(id: "seen-2")
        ], lastPromptType: "math")
        let body = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(request)) as? [String: Any])
        XCTAssertEqual(body["limit"] as? Int, 1)
        XCTAssertEqual(Set(try XCTUnwrap(body["types"] as? [String])), Set(["math", "trivia", "riddle"]))
        XCTAssertEqual(request.types.last, "math")
        XCTAssertEqual(body["excludeIds"] as? [String], ["seen-1", "seen-2"])
        let general = RealtimeContentRequest(quizOnly: false, shown: [], lastPromptType: "joke")
        XCTAssertEqual(Set(general.types), Set(InteractionRules.contentTypes))
        XCTAssertEqual(general.types.count, 6)
        XCTAssertEqual(general.types.last, "joke")
    }

    func testRealtimeRequestCapsExclusionsAtMostRecentFiveHundred() {
        let shown = (0..<600).map { InteractionShownContent(id: "seen-\($0)") }
        let request = RealtimeContentRequest(quizOnly: false, shown: shown)
        XCTAssertEqual(request.excludeIds.count, 500)
        XCTAssertEqual(request.excludeIds.first, "seen-0")
        XCTAssertEqual(request.excludeIds.last, "seen-499")
    }

    func testRealtimePayloadRejectsExtraItemsAndWrongTypesButAllowsEmptyCatalog() {
        let request = RealtimeContentRequest(quizOnly: true, shown: [])
        func payload(_ items: [InteractionContentItem]) -> SignedContentPayload {
            SignedContentPayload(schemaVersion: 1, kind: "batch", catalogVersion: 1,
                catalogUpdatedAt: InteractionTimestamp.string(from: Date()), items: items, disabledIds: [])
        }
        var other = validItem()
        other.id = "math:item-2"
        XCTAssertTrue(InteractionRules.isValidRealtimePayload(payload([]), request: request))
        XCTAssertTrue(InteractionRules.isValidRealtimePayload(payload([validItem()]), request: request))
        XCTAssertFalse(InteractionRules.isValidRealtimePayload(payload([validItem(), other]), request: request))
        other.type = "joke"
        XCTAssertFalse(InteractionRules.isValidRealtimePayload(payload([other]), request: request))
    }

    private func validItem() -> InteractionContentItem {
        InteractionContentItem(
            id: "math:item-1",
            type: "math",
            revision: 1,
            prompt: "2 + 2 等于多少？",
            answer: "4",
            explanation: "基础加法",
            choices: ["3", "4"],
            tags: ["math"],
            difficulty: 1,
            locale: "zh-CN"
        )
    }
}
