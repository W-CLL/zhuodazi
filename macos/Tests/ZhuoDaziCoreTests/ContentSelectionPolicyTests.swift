import Foundation
import XCTest
@testable import ZhuoDaziCore

final class ContentSelectionPolicyTests: XCTestCase {
    func testContentAndPetSourceLimitsRejectTheWholeAdditionalSelection() {
        XCTAssertTrue(ContentSelectionPolicy.canEnable(
            currentEnabledCount: 8,
            additionalCount: 2,
            maximum: ContentSelectionPolicy.maximumEnabledContent
        ))
        XCTAssertFalse(ContentSelectionPolicy.canEnable(
            currentEnabledCount: 9,
            additionalCount: 2,
            maximum: ContentSelectionPolicy.maximumEnabledContent
        ))
        XCTAssertFalse(ContentSelectionPolicy.canEnable(
            currentEnabledCount: 3,
            additionalCount: 1,
            maximum: ContentSelectionPolicy.maximumEnabledPetSources
        ))
    }

    func testIdentifierNormalizationKeepsOrderAndRemovesInvalidDuplicates() {
        let normalized = ContentSelectionPolicy.normalizedIdentifiers(
            ["local-a", "missing", "local-a", "local-b", "local-c"],
            validIdentifiers: ["local-a", "local-b", "local-c"],
            limit: 2
        )
        XCTAssertEqual(normalized, ["local-a", "local-b"])
    }

    func testEnabledWordPacksMergeAndDeduplicateByAction() {
        let merged = ContentSelectionPolicy.mergedWords(from: [
            ["happy": ["你好", "一起摸鱼"]],
            ["happy": ["一起摸鱼", "今天也要开心"], "grab": ["慢一点"]]
        ], action: "happy")
        XCTAssertEqual(merged, ["你好", "一起摸鱼", "今天也要开心"])
    }

    func testConversationDurationsScaleAndRemainBounded() {
        XCTAssertEqual(ConversationPresentationTiming.theaterLine("短句"), 5)
        XCTAssertGreaterThan(ConversationPresentationTiming.theaterLine(String(repeating: "长", count: 20)), 5)
        XCTAssertEqual(ConversationPresentationTiming.theaterLine(String(repeating: "长", count: 100)), 12)

        XCTAssertEqual(ConversationPresentationTiming.hallVisit("来串门"), 12)
        XCTAssertGreaterThan(ConversationPresentationTiming.hallVisit(String(repeating: "留言", count: 15)), 12)
        XCTAssertEqual(ConversationPresentationTiming.hallVisit(String(repeating: "留言", count: 100)), 20)
    }

    func testControlledUninstallReceiptRequiresFreshUuidBoundToken() {
        let valid = ControlledUninstallReceipt(
            completionEventId: UUID().uuidString,
            intentEventId: UUID().uuidString,
            completionToken: String(repeating: "a", count: 43)
        )
        XCTAssertTrue(valid.isValid())
        XCTAssertTrue(ControlledUninstallContract.isValidCompletionToken(String(repeating: "Z", count: 40)))
        XCTAssertFalse(ControlledUninstallContract.isValidCompletionToken("not/a/token"))

        let stale = ControlledUninstallReceipt(
            completionEventId: UUID().uuidString,
            intentEventId: UUID().uuidString,
            completionToken: String(repeating: "b", count: 43),
            issuedAt: Date().addingTimeInterval(-3 * 60 * 60)
        )
        XCTAssertFalse(stale.isValid())
    }

    func testControlledUninstallLaunchReceiptBindsTheInstallBatchWithoutRequiringNetwork() {
        let offline = ControlledUninstallLaunchReceipt(
            installBatchId: UUID().uuidString,
            completion: nil
        )
        XCTAssertTrue(offline.isValid())

        let invalidBatch = ControlledUninstallLaunchReceipt(
            installBatchId: "not-a-batch-id",
            completion: nil
        )
        XCTAssertFalse(invalidBatch.isValid())
    }
}
