import XCTest
@testable import ZhuoDaziCore

final class InteractionCoreTests: XCTestCase {
    func testScheduleBoundsMatchInteractionModes() {
        XCTAssertEqual(InteractionRules.scheduleBounds(for: "quiet"), 90...240)
        XCTAssertEqual(InteractionRules.scheduleBounds(for: "standard"), 45...120)
        XCTAssertEqual(InteractionRules.scheduleBounds(for: "lively"), 20...60)
        XCTAssertEqual(InteractionRules.scheduleBounds(for: "unknown"), 45...120)
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

    func testCacheNormalizationKeepsNewestItemsAndValidEvents() {
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

        let normalized = InteractionRules.normalizeCache(document, now: now)
        XCTAssertEqual(normalized.items.count, 1)
        XCTAssertEqual(normalized.items.first?.revision, 2)
        XCTAssertEqual(normalized.shown.map(\.id), [old.id])
        XCTAssertEqual(normalized.pendingEvents.count, 1)
        XCTAssertEqual(normalized.interactionMode, "standard")
        XCTAssertNil(normalized.lastPromptType)
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
