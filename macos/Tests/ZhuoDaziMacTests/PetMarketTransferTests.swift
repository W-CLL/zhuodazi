import Foundation
import XCTest
import ZhuoDaziCore
@testable import ZhuoDaziMac

/// Deterministic network fixtures: no activated account, claim, or real service
/// is used. Chunks arrive over time so cancellation and UI responsiveness are
/// exercised while a transfer is still in flight.
private final class PetMarketTransferProtocol: URLProtocol {
    struct Scenario {
        var data: Data
        var status = 200
        var declaredLength: Int?
        var chunkSize = 64 * 1024
        var delay: TimeInterval = 0
        var responseURL: URL?
        var redirectURL: URL?
        var error: URLError?
    }
    private static let registryLock = NSLock()
    private static var scenarios: [String: Scenario] = [:]
    private static var stopped = Set<String>()
    private static var requests: [String: URLRequest] = [:]
    private let delivery = DispatchQueue(label: "pet-market-transfer-fixture")
    private let lock = NSLock()
    private var isStopped = false

    static func register(_ scenario: Scenario, at url: URL) {
        registryLock.lock()
        scenarios[url.absoluteString] = scenario
        registryLock.unlock()
    }

    static func wasStopped(_ url: URL) -> Bool {
        registryLock.lock(); defer { registryLock.unlock() }
        return stopped.contains(url.absoluteString)
    }

    static func request(at url: URL) -> URLRequest? {
        registryLock.lock(); defer { registryLock.unlock() }
        return requests[url.absoluteString]
    }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        Self.registryLock.lock()
        let scenario = Self.scenarios[request.url!.absoluteString]
        Self.requests[request.url!.absoluteString] = request
        Self.registryLock.unlock()
        guard let scenario else { client?.urlProtocol(self, didFailWithError: URLError(.badURL)); return }
        var headers: [String: String] = [:]
        if let length = scenario.declaredLength { headers["Content-Length"] = String(length) }
        let response = HTTPURLResponse(url: scenario.responseURL ?? request.url!, statusCode: scenario.status,
            httpVersion: "HTTP/1.1", headerFields: headers)!
        if let redirect = scenario.redirectURL {
            client?.urlProtocol(self, wasRedirectedTo: URLRequest(url: redirect), redirectResponse: response)
            return
        }
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        deliver(scenario, offset: 0)
    }

    private func deliver(_ scenario: Scenario, offset: Int) {
        delivery.asyncAfter(deadline: .now() + scenario.delay) { [weak self] in
            guard let self else { return }
            self.lock.lock()
            let stopped = self.isStopped
            self.lock.unlock()
            guard !stopped else { return }
            if offset < scenario.data.count {
                let end = min(scenario.data.count, offset + scenario.chunkSize)
                self.client?.urlProtocol(self, didLoad: scenario.data.subdata(in: offset..<end))
                self.deliver(scenario, offset: end)
            } else if let error = scenario.error {
                self.client?.urlProtocol(self, didFailWithError: error)
            } else {
                self.client?.urlProtocolDidFinishLoading(self)
            }
        }
    }

    override func stopLoading() {
        lock.lock(); isStopped = true; lock.unlock()
        Self.registryLock.lock()
        Self.stopped.insert(request.url!.absoluteString)
        Self.registryLock.unlock()
    }
}

final class PetMarketTransferTests: XCTestCase {
    private let origin = URL(string: "https://pet-market.test")!

    private func fixture(_ scenario: PetMarketTransferProtocol.Scenario, maximum: Int,
                         policy: PetMarketTransferPolicy = .asset) -> (URL, PetMarketTransfer) {
        let url = origin.appendingPathComponent(UUID().uuidString)
        PetMarketTransferProtocol.register(scenario, at: url)
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [PetMarketTransferProtocol.self]
        return (url, PetMarketTransfer(origin: origin, maximum: maximum, policy: policy, configuration: configuration))
    }

    @MainActor
    func testLargeThrottledPackCompletesWithoutBlockingMainActor() async throws {
        let payload = Data(repeating: 0x61, count: 32 * 1024 * 1024)
        let (url, transfer) = fixture(.init(data: payload, declaredLength: payload.count,
            chunkSize: 256 * 1024, delay: 0.002), maximum: payload.count)
        var pulses = 0
        var lastPulse = Date()
        var largestGap: TimeInterval = 0
        let heartbeat = Task { @MainActor in
            while !Task.isCancelled {
                try await Task.sleep(nanoseconds: 5_000_000)
                let now = Date()
                largestGap = max(largestGap, now.timeIntervalSince(lastPulse))
                lastPulse = now
                pulses += 1
            }
        }
        defer { heartbeat.cancel() }
        let started = Date()
        let actual = try await transfer.data(for: URLRequest(url: url))
        let elapsed = Date().timeIntervalSince(started)
        XCTAssertEqual(actual, payload)
        XCTAssertGreaterThan(pulses, 5, "The main actor must keep serving UI work during a large transfer")
        XCTAssertLessThan(largestGap, 1, "A chunk must not cause a long main-thread stall")
        XCTAssertLessThan(elapsed, 8, "Regression: per-byte async iteration makes a 32 MB local transfer take many seconds")
    }

    func testCancellationStopsAnIncompleteSlowDownload() async throws {
        let (url, transfer) = fixture(.init(data: Data(repeating: 0x61, count: 4 * 1024 * 1024),
            chunkSize: 16 * 1024, delay: 0.01), maximum: 4 * 1024 * 1024)
        let work = Task { try await transfer.data(for: URLRequest(url: url)) }
        try await Task.sleep(nanoseconds: 50_000_000)
        work.cancel()
        do { _ = try await work.value; XCTFail("Canceled download returned a partial package") }
        catch is CancellationError {}
        catch { XCTFail("Expected cooperative cancellation, got \(error)") }
        for _ in 0..<20 {
            if PetMarketTransferProtocol.wasStopped(url) { break }
            try await Task.sleep(nanoseconds: 5_000_000)
        }
        XCTAssertTrue(PetMarketTransferProtocol.wasStopped(url))
    }

    func testCancellationBeforeStartDoesNotCreateANetworkRequest() async throws {
        let (url, transfer) = fixture(.init(data: Data([1])), maximum: 1)
        let work = Task {
            withUnsafeCurrentTask { $0?.cancel() }
            return try await transfer.data(for: URLRequest(url: url))
        }
        do { _ = try await work.value; XCTFail("Pre-canceled transfer succeeded") }
        catch is CancellationError {}
        XCTAssertNil(PetMarketTransferProtocol.request(at: url))
    }

    func testDeclaredAndChunkedBodiesCannotExceedSignedSize() async throws {
        for declared in [nil, 1025] as [Int?] {
            let (url, transfer) = fixture(.init(data: Data(repeating: 0x61, count: 1025), declaredLength: declared,
                chunkSize: 257), maximum: 1024)
            do { _ = try await transfer.data(for: URLRequest(url: url)); XCTFail("Oversized package accepted") }
            catch { XCTAssertTrue(error.localizedDescription.contains("超过大小限制")) }
        }
    }

    func testHTTPErrorUsesBoundedJSONMessageAndRejectsOversizedErrorBody() async throws {
        for size in [0, 32 * 1024 + 1] {
            let payload = size == 0 ? Data("{\"error\":\"请先领取桌宠\"}".utf8) : Data(repeating: 0x61, count: size)
            let (url, transfer) = fixture(.init(data: payload, status: 403), maximum: 1)
            do { _ = try await transfer.data(for: URLRequest(url: url)); XCTFail("HTTP error accepted as a package") }
            catch { XCTAssertTrue(error.localizedDescription.contains(size == 0 ? "请先领取桌宠" : "超过大小限制")) }
        }
    }

    func testTransportErrorsPropagateInsteadOfReturningPartialArchive() async throws {
        let (url, transfer) = fixture(.init(data: Data([1, 2, 3]), error: URLError(.timedOut)), maximum: 1024)
        do { _ = try await transfer.data(for: URLRequest(url: url)); XCTFail("Timed-out body accepted") }
        catch { XCTAssertEqual((error as? URLError)?.code, .timedOut) }
    }

    func testForeignOriginResponsesAndRequestsAreRejected() async throws {
        let foreign = URL(string: "https://elsewhere.test/package")!
        let (url, transfer) = fixture(.init(data: Data([1]), responseURL: foreign), maximum: 1)
        do { _ = try await transfer.data(for: URLRequest(url: url)); XCTFail("Foreign response accepted") }
        catch { XCTAssertTrue(error.localizedDescription.contains("响应来源无效")) }
        let (_, otherTransfer) = fixture(.init(data: Data([1])), maximum: 1)
        do { _ = try await otherTransfer.data(for: URLRequest(url: foreign)); XCTFail("Foreign request sent") }
        catch { XCTAssertTrue(error.localizedDescription.contains("不属于小铺")) }
        XCTAssertNil(PetMarketTransferProtocol.request(at: foreign))
    }

    func testRedirectCannotForwardAnAuthenticatedRequest() async throws {
        let foreign = URL(string: "https://elsewhere.test/redirect-\(UUID().uuidString)")!
        let (url, transfer) = fixture(.init(data: Data(), status: 302, redirectURL: foreign), maximum: 1)
        var request = URLRequest(url: url)
        request.setValue("fixture-only-license", forHTTPHeaderField: "Authorization")
        do { _ = try await transfer.data(for: request); XCTFail("Redirect accepted") }
        catch { XCTAssertTrue(error.localizedDescription.contains("重定向")) }
        XCTAssertNil(PetMarketTransferProtocol.request(at: foreign))
    }

    func testAPITimeoutStaysShortWhileArchiveHonorsGrantDeadline() async throws {
        XCTAssertEqual(PetMarketTransferPolicy.api.idleTimeout, 25)
        XCTAssertEqual(PetMarketTransferPolicy.api.resourceTimeout, 60)
        let archive = try PetMarketTransferPolicy.archive(validFor: 587)
        XCTAssertEqual(archive.idleTimeout, 120)
        XCTAssertEqual(archive.resourceTimeout, 587)
        let nearExpiry = try PetMarketTransferPolicy.archive(validFor: 10)
        XCTAssertEqual(nearExpiry.idleTimeout, 10)
        XCTAssertEqual(nearExpiry.resourceTimeout, 10)
        XCTAssertThrowsError(try PetMarketTransferPolicy.archive(validFor: 0))
        XCTAssertThrowsError(try PetMarketTransferPolicy.archive(validFor: .infinity))
        let (url, transfer) = fixture(.init(data: Data([1])), maximum: 1, policy: .api)
        _ = try await transfer.data(for: URLRequest(url: url))
        XCTAssertEqual(PetMarketTransferProtocol.request(at: url)?.timeoutInterval, 25)
    }
}
