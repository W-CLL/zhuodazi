import Foundation
import XCTest
@testable import ZhuoDaziMac

private final class RemoteConfigTestProtocol: URLProtocol {
    private static let lock = NSLock()
    private static var responseData = Data()
    private static var status = 200
    private static var failure: URLError?
    private static var recordedRequest: URLRequest?

    static func respond(with data: Data, statusCode: Int = 200, error: URLError? = nil) {
        lock.lock()
        defer { lock.unlock() }
        responseData = data
        status = statusCode
        failure = error
        recordedRequest = nil
    }

    static func lastRequest() -> URLRequest? {
        lock.lock()
        defer { lock.unlock() }
        return recordedRequest
    }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        Self.lock.lock()
        Self.recordedRequest = request
        let data = Self.responseData, statusCode = Self.status, error = Self.failure
        Self.lock.unlock()
        if let error {
            client?.urlProtocol(self, didFailWithError: error)
            return
        }
        let response = HTTPURLResponse(url: request.url!, statusCode: statusCode,
            httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: data)
        client?.urlProtocolDidFinishLoading(self)
    }
    override func stopLoading() {}
}

final class RemoteConfigServiceTests: XCTestCase {
    private let cacheKey = "com.zhuodazi.desktop-pet.remote-config.v1"
    private let cachedData = Data("""
        {"activation":{"title":"之前的首次标题","description":"之前的首次说明",
        "replacementTitle":"之前的更换标题","replacementDescription":"之前的更换说明"}}
        """.utf8)
    private let latestData = Data("""
        {"wechatId":"author_123","activation":{"title":"最新的首次标题","description":"最新的首次说明",
        "replacementTitle":"最新的更换标题","replacementDescription":"最新的更换说明"}}
        """.utf8)

    @MainActor
    private func withService(_ action: @MainActor (RemoteConfigService, UserDefaults, URLSession) async throws -> Void) async throws {
        let suite = "remote-config-tests-\(UUID().uuidString)", defaults = UserDefaults(suiteName: suite)!
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [RemoteConfigTestProtocol.self]
        let session = URLSession(configuration: configuration)
        defaults.set(cachedData, forKey: cacheKey)
        defer {
            session.invalidateAndCancel()
            defaults.removePersistentDomain(forName: suite)
        }
        try await action(RemoteConfigService(defaults: defaults, session: session), defaults, session)
    }

    func testFreshRequestUpdatesBothActivationModesAndPersistsForNextOpening() async throws {
        RemoteConfigTestProtocol.respond(with: latestData)
        try await withService { service, defaults, session in
            XCTAssertEqual(service.current.activationTitle, "之前的首次标题")
            let refreshed = await service.refresh()
            XCTAssertTrue(refreshed)
            XCTAssertEqual(service.current.activationTitle, "最新的首次标题")
            XCTAssertEqual(service.current.activationDescription, "最新的首次说明")
            XCTAssertEqual(service.current.activationReplacementTitle, "最新的更换标题")
            XCTAssertEqual(service.current.activationReplacementDescription, "最新的更换说明")
            XCTAssertEqual(service.current.wechatId, "author_123")
            XCTAssertEqual(RemoteConfigTestProtocol.lastRequest()?.cachePolicy, .reloadIgnoringLocalCacheData)
            XCTAssertEqual(RemoteConfigTestProtocol.lastRequest()?.value(forHTTPHeaderField: "User-Agent"), "ZhuoDazi/\(AppVersion.current)")
            XCTAssertEqual(RemoteConfigTestProtocol.lastRequest()?.value(forHTTPHeaderField: "Cache-Control"), "no-cache")
            XCTAssertEqual(RemoteConfigService(defaults: defaults, session: session).current, service.current)
        }
    }

    func testOfflineHTTPFailureAndMalformedResponseKeepPreviouslyValidCopy() async throws {
        try await withService { service, defaults, _ in
            let cached = service.current
            for failure in 0..<4 {
                switch failure {
                case 0: RemoteConfigTestProtocol.respond(with: latestData, error: URLError(.notConnectedToInternet))
                case 1: RemoteConfigTestProtocol.respond(with: latestData, statusCode: 503)
                case 2: RemoteConfigTestProtocol.respond(with: Data("invalid JSON".utf8))
                default: RemoteConfigTestProtocol.respond(with: Data(repeating: 0x20, count: 1024 * 1024 + 1))
                }
                let refreshed = await service.refresh()
                XCTAssertFalse(refreshed)
                XCTAssertEqual(service.current, cached)
                XCTAssertEqual(defaults.data(forKey: cacheKey), cachedData)
            }
        }
    }

    func testBlankActivationFieldsFallBackAndConfiguredLineBreaksSurvive() async throws {
        RemoteConfigTestProtocol.respond(with: Data("""
            {"activation":{"title":"   ","description":"第一行\\n第二行","replacementTitle":"","replacementDescription":" "}}
            """.utf8))
        try await withService { service, _, _ in
            let refreshed = await service.refresh()
            XCTAssertTrue(refreshed)
            XCTAssertEqual(service.current.activationTitle, RemoteConfig().activationTitle)
            XCTAssertEqual(service.current.activationDescription, "第一行\n第二行")
            XCTAssertEqual(service.current.activationReplacementTitle, RemoteConfig().activationReplacementTitle)
            XCTAssertEqual(service.current.activationReplacementDescription, RemoteConfig().activationReplacementDescription)
        }
    }

    @MainActor
    func testCanceledRefreshDoesNotReplaceCachedContent() async throws {
        RemoteConfigTestProtocol.respond(with: latestData)
        try await withService { service, defaults, _ in
            let cached = service.current
            let work = Task { @MainActor in await service.refresh() }
            work.cancel()
            let refreshed = await work.value
            XCTAssertFalse(refreshed)
            XCTAssertEqual(service.current, cached)
            XCTAssertEqual(defaults.data(forKey: cacheKey), cachedData)
        }
    }
}
