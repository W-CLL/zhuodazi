import AppKit
import Security

struct PaymentCatalog: Decodable {
    struct Activation: Decodable { let title: String; let description: String; let amountFen: Int }
    struct Provider: Decodable { let configured: Bool }
    let activation: Activation
    let provider: Provider
}

struct CheckoutOrder: Codable {
    let id: String
    let kind: String
    let title: String
    let amountFen: Int
    let status: String
    let fulfillmentStatus: String?
    let productId: String?
    let productVersion: String?
    let createdAt: Date
    let expiresAt: Date
}

struct CheckoutResponse: Codable {
    var order: CheckoutOrder
    var checkoutUrl: String?
}

/// Uses the device's existing identity. Only the server decides price, payment and entitlement.
@MainActor
final class PaymentCheckoutClient {
    private let licenses: LicenseService
    private let configuration: URLSessionConfiguration
    private let openURL: (URL) -> Bool
    private let readSaved: () throws -> Data?
    private let writeSaved: (Data) throws -> Void
    private var pending: [String: CheckoutResponse] = [:]
    private var nextRefresh: [String: Date] = [:]
    private var busyOrders = Set<String>()

    init(licenses: LicenseService, configuration: URLSessionConfiguration = .ephemeral,
         openURL: @escaping (URL) -> Bool = { NSWorkspace.shared.open($0) },
         readSaved: (() throws -> Data?)? = nil, writeSaved: ((Data) throws -> Void)? = nil) {
        self.licenses = licenses; self.configuration = configuration; self.openURL = openURL
        let store = CheckoutKeychain(account: "payment-orders-\(licenses.installationId)")
        self.readSaved = readSaved ?? { try store.read() }
        self.writeSaved = writeSaved ?? { try store.write($0) }
        if let bytes = try? self.readSaved(), bytes.count <= 2 * 1024 * 1024,
           let saved = try? JSONDecoder().decode([CheckoutResponse].self, from: bytes) {
            for item in saved.prefix(100) where (try? Self.checkoutURL(item.checkoutUrl ?? "", orderId: item.order.id)) != nil {
                pending[item.order.id] = item
            }
        }
    }

    static func checkoutURL(_ value: String, orderId: String) throws -> URL {
        guard let expected = UUID(uuidString: orderId), let url = URL(string: value),
              url.scheme == "https", url.host == DeskPetApi.host, url.port == nil || url.port == 443,
              url.user == nil, url.password == nil, url.query == nil,
              url.path.hasPrefix("/checkout/alipay/"), url.pathComponents.count == 4,
              let actual = UUID(uuidString: url.lastPathComponent), actual == expected,
              let fragment = url.fragment,
              fragment.range(of: "^token=[A-Za-z0-9_-]{20,256}$", options: .regularExpression) != nil else {
            throw PetMarketError.invalid("收银台地址无效")
        }
        return url
    }

    func catalog() async throws -> PaymentCatalog {
        if !licenses.isActivated { _ = try await licenses.checkTrial() }
        return try await send("catalog")
    }

    @discardableResult
    func purchase(activation: Bool, productId: String? = nil) async throws -> CheckoutResponse {
        if !activation {
            guard licenses.isActivated, let productId, PetMarketJSON.validPart(productId) else {
                throw PetMarketError.invalid("请先激活桌搭子")
            }
        }
        var body = ["kind": activation ? "activation" : "pet"]
        if !activation { body["productId"] = productId }
        let response: CheckoutResponse = try await send("orders", method: "POST", body: body)
        _ = try Self.checkoutURL(response.checkoutUrl ?? "", orderId: response.order.id)
        pending[response.order.id] = response
        try save()
        try reopen(response.order.id)
        return response
    }

    func reopen(_ id: String) throws {
        guard let saved = pending[id] else { throw PetMarketError.invalid("请重新选择商品购买") }
        let url = try Self.checkoutURL(saved.checkoutUrl ?? "", orderId: id)
        guard openURL(url) else { throw PetMarketError.invalid("无法打开浏览器，请稍后重试") }
    }

    func refreshActivations() async throws {
        for saved in pending.values.filter({ $0.order.kind == "activation" }) {
            _ = try await update(saved)
        }
        try save()
    }

    func pendingPetOrders() async throws -> [PetMarketOrder] {
        var orders: [PetMarketOrder] = []
        for saved in pending.values.filter({ $0.order.kind == "pet" }) {
            let response = try await update(saved)
            let order = response.order
            // Fulfilled purchases are loaded from the original market API; installation still verifies signatures.
            if order.status == "paid", order.fulfillmentStatus == "fulfilled" { continue }
            guard let productId = order.productId, let version = order.productVersion else { continue }
            orders.append(PetMarketOrder(id: order.id, productId: productId, productTitle: order.title,
                productVersion: version, status: ["pending", "paid"].contains(order.status) ? "pending_payment" : "expired",
                amountFen: order.amountFen, currency: "CNY", paymentReference: "alipay:\(order.id)",
                payment: PetMarketPayment(enabled: true, available: true, method: "alipay_page", recipientName: "",
                    recipientAccount: "", qrCodeDataUrl: "", instructions: "", unavailableReason: "", listenerOnline: true),
                createdAt: order.createdAt, expiresAt: order.expiresAt))
        }
        try save()
        return orders
    }

    private func update(_ saved: CheckoutResponse) async throws -> CheckoutResponse {
        let id = saved.order.id
        guard UUID(uuidString: id) != nil else { throw PetMarketError.invalid("订单编号无效") }
        guard busyOrders.insert(id).inserted else { return pending[id] ?? saved }
        defer { busyOrders.remove(id) }
        var response: CheckoutResponse = try await send("orders/\(id)")
        if (["pending", "expired"].contains(response.order.status) || response.order.status == "paid" && response.order.fulfillmentStatus != "fulfilled"),
           Date() >= (nextRefresh[id] ?? .distantPast) {
            nextRefresh[id] = Date().addingTimeInterval(30)
            do { response = try await send("orders/\(id)/refresh", method: "POST") }
            catch is CancellationError { throw CancellationError() }
            catch { /* The authenticated GET remains usable when an official query is unavailable. */ }
        }
        guard response.order.id == id else { throw PetMarketError.invalid("订单编号不匹配") }
        response.checkoutUrl = saved.checkoutUrl
        if response.order.status == "paid", response.order.fulfillmentStatus == "fulfilled" { pending[id] = nil }
        else { pending[id] = response }
        return response
    }

    private func send<T: Decodable>(_ path: String, method: String = "GET", body: [String: String]? = nil) async throws -> T {
        let url = DeskPetApi.baseURL.appendingPathComponent("api/payments/alipay/\(path)")
        var request = URLRequest(url: url)
        request.httpMethod = method
        let authorization: String
        if licenses.isActivated, let id = licenses.marketLicenseId { authorization = "Bearer \(id).\(licenses.installationCredential)" }
        else { authorization = "Trial \(licenses.installationId).\(licenses.installationCredential)" }
        request.setValue(authorization, forHTTPHeaderField: "Authorization")
        request.setValue(licenses.installationId, forHTTPHeaderField: "X-DeskPet-Installation-Id")
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
        request.setValue(AppVersion.current, forHTTPHeaderField: "X-DeskPet-Version")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let body {
            request.httpBody = try JSONSerialization.data(withJSONObject: body)
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        let bytes = try await PetMarketTransfer(origin: DeskPetApi.baseURL, maximum: 2 * 1024 * 1024,
            policy: .api, configuration: configuration).data(for: request)
        return try PetMarketJSON.decoder().decode(T.self, from: bytes)
    }

    private func save() throws {
        let saved = pending.values.sorted { $0.order.createdAt > $1.order.createdAt }.prefix(100)
        try writeSaved(JSONEncoder().encode(Array(saved)))
    }
}

private struct CheckoutKeychain {
    let account: String
    private var query: [String: Any] { [kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: Bundle.main.bundleIdentifier ?? "com.zhuodazi.desktop-pet", kSecAttrAccount as String: account] }
    func read() throws -> Data? {
        var request = query; request[kSecReturnData as String] = true; request[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        let status = SecItemCopyMatching(request as CFDictionary, &item)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw LicenseError.keychain(status) }
        return item as? Data
    }
    func write(_ data: Data) throws {
        let values: [String: Any] = [kSecValueData as String: data]
        var status = SecItemUpdate(query as CFDictionary, values as CFDictionary)
        if status == errSecItemNotFound {
            var item = query; item[kSecValueData as String] = data
            item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            status = SecItemAdd(item as CFDictionary, nil)
        }
        guard status == errSecSuccess else { throw LicenseError.keychain(status) }
    }
}
