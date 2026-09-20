import XCTest
@testable import ZhuoDaziCore

final class PresentationLifetimeTests: XCTestCase {
    func testExpiredBubbleCannotHideTheBubbleThatReplacedIt() {
        var lifetime = PresentationLifetime()
        let firstBubble = lifetime.begin()
        let replacementBubble = lifetime.begin()
        XCTAssertFalse(lifetime.finish(firstBubble))
        XCTAssertTrue(lifetime.isCurrent(replacementBubble))
        XCTAssertTrue(lifetime.finish(replacementBubble))
    }

    func testOldChoiceAndCloseEventsCannotCompleteTheNextInteractionCard() {
        var lifetime = PresentationLifetime()
        let question = lifetime.begin()
        XCTAssertTrue(lifetime.finish(question))
        let answer = lifetime.begin()
        XCTAssertFalse(lifetime.finish(question))
        XCTAssertTrue(lifetime.isCurrent(answer))
        XCTAssertTrue(lifetime.finish(answer))
        XCTAssertFalse(lifetime.finish(answer))
    }

    func testCanceledSceneCleanupCannotDismissAReplacementPresentation() {
        var lifetime = PresentationLifetime()
        let theaterLine = lifetime.begin()
        lifetime.invalidate()
        let laterStatus = lifetime.begin()
        XCTAssertFalse(lifetime.finish(theaterLine))
        XCTAssertTrue(lifetime.isCurrent(laterStatus))
    }

    func testClosingWithoutAReplacementInvalidatesAllPendingEvents() {
        var lifetime = PresentationLifetime()
        let presentation = lifetime.begin()
        lifetime.invalidate()
        XCTAssertFalse(lifetime.isCurrent(presentation))
        XCTAssertFalse(lifetime.finish(presentation))
    }
}
