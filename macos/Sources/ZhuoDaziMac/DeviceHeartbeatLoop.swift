import Foundation

@MainActor
final class DeviceHeartbeatLoop {
    private let send: @MainActor () async -> Void
    private let now: () -> TimeInterval
    private var timer: Timer?
    private var sending: Task<Void, Never>?
    private var started = false
    private var suspended = false
    private var stopped = false
    private var identityRefreshPending = false
    private var lastAttempt: TimeInterval?

    init(now: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
         send: @escaping @MainActor () async -> Void) {
        self.now = now
        self.send = send
    }

    func start() {
        guard !started, !stopped else { return }
        started = true
        scheduleTimer()
        request()
    }

    func identityRefreshed() { request(force: true) }

    func suspend() {
        guard !stopped, !suspended else { return }
        suspended = true
        identityRefreshPending = false
        timer?.invalidate()
        timer = nil
        sending?.cancel()
    }

    func resume() {
        guard started, !stopped else { return }
        let wasSuspended = suspended
        if wasSuspended {
            suspended = false
            scheduleTimer()
        }
        request(force: wasSuspended)
    }

    func stop() {
        stopped = true
        identityRefreshPending = false
        timer?.invalidate()
        timer = nil
        sending?.cancel()
    }

    private func scheduleTimer() {
        timer?.invalidate()
        let timer = Timer(timeInterval: 60, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in self?.request() }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func request(force: Bool = false) {
        guard started, !stopped, !suspended else { return }
        if sending != nil {
            identityRefreshPending = identityRefreshPending || force
            return
        }
        let current = now()
        guard force || (lastAttempt.map { current - $0 >= 10 } ?? true) else { return }
        lastAttempt = current
        sending = Task { @MainActor [weak self] in
            guard let self else { return }
            if !Task.isCancelled && !stopped && !suspended { await send() }
            sending = nil
            let again = identityRefreshPending && !stopped && !suspended
            identityRefreshPending = false
            if again { request(force: true) }
        }
    }
}
