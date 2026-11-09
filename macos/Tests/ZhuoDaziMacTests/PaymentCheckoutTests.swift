import AppKit
import Foundation
import XCTest
@testable import ZhuoDaziMac

private final class CheckoutProtocol: URLProtocol {
    static let lock = NSLock()
    static var responder: ((URLRequest) -> Data)?
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        Self.lock.lock(); let handler = Self.responder; Self.lock.unlock()
        guard let handler else { client?.urlProtocol(self, didFailWithError: URLError(.badURL)); return }
        let data = handler(request)
        client?.urlProtocol(self, didReceive: HTTPURLResponse(url: request.url!, statusCode: 200,
            httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "application/json"])!, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: data); client?.urlProtocolDidFinishLoading(self)
    }
    override func stopLoading() {}
}

final class PaymentCheckoutTests: XCTestCase {
    @MainActor
    func testCheckoutRejectsUntrustedOrMismatchedURLs() throws {
        let id = UUID().uuidString.lowercased()
        let url = "https://in.desktoppet.online/checkout/alipay/\(id)#token=\(String(repeating: "t", count: 43))"
        XCTAssertEqual(try PaymentCheckoutClient.checkoutURL(url, orderId: id).absoluteString, url)
        for invalid in [url.replacingOccurrences(of: "https:", with: "http:"),
            url.replacingOccurrences(of: "in.desktoppet.online", with: "example.com"),
            url.replacingOccurrences(of: "https://", with: "https://user@"),
            url.replacingOccurrences(of: "#token=", with: "?token="),
            url.replacingOccurrences(of: id, with: UUID().uuidString), url + "&other=value",
            url.replacingOccurrences(of: "/checkout/alipay/", with: "/other/") ] {
            XCTAssertThrowsError(try PaymentCheckoutClient.checkoutURL(invalid, orderId: id))
        }
    }

    @MainActor
    func testPurchaseIdentityRecoveryAndAuthoritativeFulfillment() async throws {
        let config = PetShopPreviewConfiguration(baseUrl: "http://127.0.0.1:9/", dataDirectory: "/unused",
            accountId: UUID().uuidString, licenseId: UUID().uuidString,
            installationId: String(repeating: "a", count: 24), credential: String(repeating: "b", count: 48),
            signingPublicKeySpki: DeskPetApi.signingPublicKeySPKI)
        let licenses = try LicenseService(preview: config)
        let session = URLSessionConfiguration.ephemeral; session.protocolClasses = [CheckoutProtocol.self]
        let id = UUID().uuidString.lowercased()
        let link = "https://in.desktoppet.online/checkout/alipay/\(id)#token=\(String(repeating: "t", count: 43))"
        var saved: Data?, opened: URL?, requests: [URLRequest] = [], paid = false, refreshes = 0
        CheckoutProtocol.lock.lock()
        CheckoutProtocol.responder = { request in
            requests.append(request)
            if request.url!.path.hasSuffix("/refresh") { refreshes += 1 }
            let value: [String: Any] = ["order": ["id": id, "kind": "pet", "title": "桌宠商品", "amountFen": 380,
                "status": paid ? "paid" : "pending", "fulfillmentStatus": paid ? "fulfilled" : "pending",
                "productId": "test-pet", "productVersion": "1.0.0", "createdAt": "2026-11-09T00:00:00.000Z",
                "expiresAt": "2026-11-09T00:15:00.000Z"], "checkoutUrl": link]
            return try! JSONSerialization.data(withJSONObject: value)
        }
        CheckoutProtocol.lock.unlock()
        defer { CheckoutProtocol.lock.lock(); CheckoutProtocol.responder = nil; CheckoutProtocol.lock.unlock() }
        let checkout = PaymentCheckoutClient(licenses: licenses, configuration: session,
            openURL: { opened = $0; return true }, readSaved: { saved }, writeSaved: { saved = $0 })
        try await checkout.purchase(activation: false, productId: "test-pet")
        XCTAssertEqual(opened?.absoluteString, link)
        XCTAssertTrue(requests.allSatisfy { $0.url?.scheme == "https" && $0.url?.host == DeskPetApi.host })
        XCTAssertEqual(requests.first?.value(forHTTPHeaderField: "Authorization"), "Bearer \(config.licenseId).\(config.credential)")
        let pending = try await checkout.pendingPetOrders()
        XCTAssertEqual(pending.first?.status, "pending_payment")
        XCTAssertEqual(pending.first?.payment?.method, "alipay_page")
        _ = try await checkout.pendingPetOrders()
        XCTAssertEqual(refreshes, 1, "Official query must be throttled while normal order polling continues")
        let restored = PaymentCheckoutClient(licenses: licenses, configuration: session,
            openURL: { opened = $0; return true }, readSaved: { saved }, writeSaved: { saved = $0 })
        opened = nil; try restored.reopen(id); XCTAssertEqual(opened?.absoluteString, link)
        paid = true
        let completed = try await restored.pendingPetOrders()
        XCTAssertTrue(completed.isEmpty, "Fulfilled orders must come from the original market history and signed download APIs")
    }
}
