import Foundation

/// Local learning progress. Starting an activity never implies it succeeded.
public struct GuideProgress: Codable, Equatable {
    public var version = 1
    public var step = 0
    public var dismissed = false
    public var completedSteps: [Int] = []
    public var skippedSteps: [Int] = []

    public init() {}

    public static var legacyUpgrade: GuideProgress {
        var value = GuideProgress()
        value.dismissed = true
        return value
    }

    public var shouldAutoPresent: Bool { !dismissed && step < 5 }
    public var isDone: Bool { step == 5 }

    public mutating func resume() {
        dismissed = false
        if step == 0 { step = 1 }
    }

    public mutating func recordCompletion(_ completedStep: Int) {
        guard (1...4).contains(completedStep) else { return }
        if !completedSteps.contains(completedStep) { completedSteps.append(completedStep) }
        skippedSteps.removeAll { $0 == completedStep }
    }

    public mutating func advance(skipping: Bool = false) {
        guard (1...4).contains(step) else { return }
        if skipping {
            if !completedSteps.contains(step), !skippedSteps.contains(step) { skippedSteps.append(step) }
        } else {
            guard completedSteps.contains(step) else { return }
        }
        step += 1
        if step == 5 { dismissed = true }
    }

    public mutating func replay() {
        self = GuideProgress()
        step = 1
    }

    public mutating func normalize() {
        step = min(5, max(0, step))
        completedSteps = Array(Set(completedSteps.filter { (1...4).contains($0) })).sorted()
        skippedSteps = Array(Set(skippedSteps.filter { (1...4).contains($0) && !completedSteps.contains($0) })).sorted()
    }
}
