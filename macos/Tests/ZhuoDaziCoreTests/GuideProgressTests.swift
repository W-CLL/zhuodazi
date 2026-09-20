import XCTest
@testable import ZhuoDaziCore

final class GuideProgressTests: XCTestCase {
    func testNewInstallOffersWelcomeWhileExistingInstallDoesNotReplayAutomatically() {
        XCTAssertEqual(GuideProgress().step, 0)
        XCTAssertTrue(GuideProgress().shouldAutoPresent)
        XCTAssertFalse(GuideProgress.legacyUpgrade.shouldAutoPresent)
        XCTAssertTrue(GuideProgress.legacyUpgrade.dismissed)
    }

    func testAttemptingInteractionWithoutAUserResponseCannotAdvance() {
        var progress = GuideProgress()
        progress.step = 2
        progress.advance()
        XCTAssertEqual(progress.step, 2)
        XCTAssertTrue(progress.completedSteps.isEmpty)
        progress.recordCompletion(2)
        progress.advance()
        XCTAssertEqual(progress.step, 3)
        XCTAssertEqual(progress.completedSteps, [2])
    }

    func testSkippingRecordsIntentWithoutClaimingAnExperienceWasCompleted() {
        var progress = GuideProgress()
        progress.resume()
        progress.advance(skipping: true)
        XCTAssertEqual(progress.step, 2)
        XCTAssertEqual(progress.skippedSteps, [1])
        XCTAssertTrue(progress.completedSteps.isEmpty)
    }

    func testClosingAndRestartingPreservesTheCurrentStepUntilExplicitResume() throws {
        var progress = GuideProgress()
        progress.step = 3
        progress.completedSteps = [1, 2]
        progress.dismissed = true
        let saved = try JSONEncoder().encode(progress)
        var restored = try JSONDecoder().decode(GuideProgress.self, from: saved)
        XCTAssertFalse(restored.shouldAutoPresent)
        restored.resume()
        XCTAssertEqual(restored.step, 3)
        XCTAssertEqual(restored.completedSteps, [1, 2])
        XCTAssertTrue(restored.shouldAutoPresent)
    }

    func testInterruptedTheaterNeedsACompletedReplayBeforeAdvancing() {
        var progress = GuideProgress()
        progress.step = 3
        progress.dismissed = true
        progress.resume()
        progress.advance()
        XCTAssertEqual(progress.step, 3)
        XCTAssertTrue(progress.completedSteps.isEmpty)
        progress.recordCompletion(3)
        XCTAssertEqual(progress.completedSteps, [3])
        progress.advance()
        XCTAssertEqual(progress.step, 4)
    }

    func testDoneDoesNotAutoReplayAndReplayResetsOnlyLearningProgress() {
        var progress = GuideProgress()
        progress.step = 4
        progress.recordCompletion(4)
        progress.advance()
        XCTAssertTrue(progress.isDone)
        XCTAssertFalse(progress.shouldAutoPresent)
        progress.replay()
        XCTAssertEqual(progress.step, 1)
        XCTAssertEqual(progress.version, 1)
        XCTAssertTrue(progress.completedSteps.isEmpty)
        XCTAssertTrue(progress.skippedSteps.isEmpty)
        XCTAssertTrue(progress.shouldAutoPresent)
    }

    func testNormalizationKeepsDisjointValidMilestonesAndSafeCardIndex() {
        var progress = GuideProgress()
        progress.step = 999
        progress.completedSteps = [0, 2, 2, 6]
        progress.skippedSteps = [-1, 1, 1, 2, 9]
        progress.normalize()
        XCTAssertEqual(progress.step, 5)
        XCTAssertEqual(progress.completedSteps, [2])
        XCTAssertEqual(progress.skippedSteps, [1])
    }
}
