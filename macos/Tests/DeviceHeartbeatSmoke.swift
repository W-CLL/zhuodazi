import Foundation

/// Exercises the production scheduler without opening the app, reading credentials,
/// touching user settings, or making network requests.
@main
struct DeviceHeartbeatSmoke {
    enum Failure: Error { case expectation(String) }

    @MainActor private static var checks = 0

    @MainActor
    private static func require(_ condition: @autoclosure () -> Bool, _ message: String) throws {
        guard condition() else { throw Failure.expectation(message) }
        checks += 1
    }

    @MainActor
    private static func waitFor(_ condition: () -> Bool) async throws {
        for _ in 0..<200 {
            if condition() { return }
            try await Task.sleep(for: .milliseconds(5))
        }
        throw Failure.expectation("Timed out waiting for the heartbeat task")
    }

    @MainActor
    private final class Probe {
        var time: TimeInterval = 0
        var calls = 0
        var active = 0
        var maximumActive = 0
        var cancelled = 0
        private var pending: [CheckedContinuation<Void, Never>] = []

        func send() async {
            calls += 1
            active += 1
            maximumActive = max(maximumActive, active)
            await withCheckedContinuation { pending.append($0) }
            if Task.isCancelled { cancelled += 1 }
            active -= 1
        }

        func finishNext() throws {
            guard !pending.isEmpty else { throw Failure.expectation("Missing pending send") }
            pending.removeFirst().resume()
        }
    }

    @MainActor
    private static func checkSingleFlightAndCancellation() async throws {
        let probe = Probe()
        let loop = DeviceHeartbeatLoop(now: { probe.time }) { await probe.send() }
        defer { loop.stop() }
        loop.start()
        try await waitFor { probe.calls == 1 }
        try require(probe.calls == 1, "Startup sends immediately")
        loop.start()
        loop.identityRefreshed()
        loop.identityRefreshed()
        probe.time = 60
        loop.resume()
        try require(probe.calls == 1, "Repeated triggers do not overlap an in-flight request")
        try probe.finishNext()
        try await waitFor { probe.calls == 2 }
        try require(probe.maximumActive == 1, "Identity refreshes coalesce into a single follow-up")

        loop.suspend()
        loop.identityRefreshed()
        probe.time = 600
        try require(probe.calls == 2, "Suspended loop does not send")
        loop.resume()
        loop.resume()
        try require(probe.calls == 2, "Resume waits for the cancelled request to finish")
        try probe.finishNext()
        try await waitFor { probe.calls == 3 }
        try require(probe.cancelled == 1, "Sleep cancels the current task")
        try require(probe.maximumActive == 1, "Suspend and resume preserve single flight")

        loop.stop()
        try probe.finishNext()
        try await waitFor { probe.active == 0 }
        try require(probe.cancelled == 2, "Exit cancels the current task")
        loop.start()
        loop.resume()
        loop.identityRefreshed()
        try await Task.sleep(for: .milliseconds(20))
        try require(probe.calls == 3, "Stopped loop never starts again")
    }

    @MainActor
    private static func checkForegroundThrottle() async throws {
        var time: TimeInterval = 0
        var calls = 0
        let loop = DeviceHeartbeatLoop(now: { time }) { calls += 1 }
        defer { loop.stop() }
        loop.start()
        try await waitFor { calls == 1 }
        loop.resume()
        loop.resume()
        time = 9.99
        loop.resume()
        try await Task.sleep(for: .milliseconds(20))
        try require(calls == 1, "Foreground callbacks within ten seconds are throttled")
        time = 10
        loop.resume()
        try await waitFor { calls == 2 }
        try require(calls == 2, "Foreground callback sends once the throttle expires")
        loop.identityRefreshed()
        try await waitFor { calls == 3 }
        try require(calls == 3, "Fresh device registration bypasses foreground throttling")
        loop.suspend()
        loop.resume()
        try await waitFor { calls == 4 }
        try require(calls == 4, "Wake sends promptly even inside the throttle interval")
    }

    @MainActor
    private static func checkCancellationBeforeTaskStarts() async throws {
        var calls = 0
        let loop = DeviceHeartbeatLoop { calls += 1 }
        loop.start()
        loop.suspend()
        try await Task.sleep(for: .milliseconds(20))
        try require(calls == 0, "A queued task must not send after sleep")
        loop.resume()
        try await waitFor { calls == 1 }
        loop.stop()

        let stoppedLoop = DeviceHeartbeatLoop { calls += 1 }
        stoppedLoop.start()
        stoppedLoop.stop()
        try await Task.sleep(for: .milliseconds(20))
        try require(calls == 1, "A queued task must not send after exit")
    }

    @MainActor
    static func main() async throws {
        try await checkSingleFlightAndCancellation()
        try await checkForegroundThrottle()
        try await checkCancellationBeforeTaskStarts()
        print("Device heartbeat smoke passed (\(checks) assertions); no network or user data accessed.")
    }
}
