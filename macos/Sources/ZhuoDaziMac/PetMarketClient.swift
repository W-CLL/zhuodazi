import CryptoKit
import Foundation

// Redirects never receive license credentials. The same origin policy also
// applies to public previews and free resource downloads.
private final class PetMarketSessionDelegate: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
}

@MainActor
final class PetMarketClient {
    let licenses: LicenseService
    let origin: URL
    let storage: URL
    let isPreview: Bool
    private let publicKey: Curve25519.Signing.PublicKey
    private let delegate = PetMarketSessionDelegate()
    private lazy var session: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 25
        configuration.timeoutIntervalForResource = 120
        configuration.httpCookieStorage = nil
        configuration.urlCache = nil
        configuration.connectionProxyDictionary = [:]
        return URLSession(configuration: configuration, delegate: delegate, delegateQueue: nil)
    }()
    private var serverOffset: TimeInterval = 0
    var serverNow: Date { Date().addingTimeInterval(serverOffset) }

    init(licenses: LicenseService, preview: PetShopPreviewConfiguration? = nil) throws {
        self.licenses = licenses
        isPreview = preview != nil
        origin = preview?.origin ?? DeskPetApi.baseURL
        storage = try preview?.storage ?? FileManager.default.url(for: .applicationSupportDirectory,
            in: .userDomainMask, appropriateFor: nil, create: true).appendingPathComponent("ZhuoDazi/Shop")
        let spki = preview?.signingPublicKeySpki ?? DeskPetApi.signingPublicKeySPKI
        guard let key = Data(base64Encoded: spki), key.count == 44,
              key.prefix(12) == Data([0x30,0x2a,0x30,0x05,0x06,0x03,0x2b,0x65,0x70,0x03,0x21,0x00]) else {
            throw PetMarketError.invalid("小铺签名公钥无效")
        }
        publicKey = try Curve25519.Signing.PublicKey(rawRepresentation: Data(key.suffix(32)))
        try PetMarketInstaller.prepareDirectory(storage)
    }

    func data(_ path: String, method: String = "GET", body: [String: String]? = nil,
              authenticated: Bool = false, limit: Int = 2 * 1024 * 1024) async throws -> Data {
        guard path.hasPrefix("/"), !path.hasPrefix("//"), !path.contains("\\"),
              let url = URL(string: path, relativeTo: origin)?.absoluteURL,
              url.scheme == origin.scheme, url.host == origin.host, url.port == origin.port,
              url.user == nil, url.password == nil, url.fragment == nil else {
            throw PetMarketError.invalid("已阻止不属于小铺的请求地址")
        }
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
        request.setValue(AppVersion.current, forHTTPHeaderField: "X-DeskPet-Version")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let body {
            request.httpBody = try JSONSerialization.data(withJSONObject: body)
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        if authenticated {
            guard licenses.isActivated else { throw LicenseError.inactive }
            try licenses.authorize(&request)
        }
        let (bytes, response) = try await session.bytes(for: request)
        guard let http = response as? HTTPURLResponse, http.url?.host == origin.host,
              http.url?.scheme == origin.scheme, http.url?.port == origin.port else {
            throw PetMarketError.invalid("小铺响应来源无效")
        }
        let success = (200..<300).contains(http.statusCode)
        let maximum = success ? limit : 32 * 1024
        guard response.expectedContentLength <= Int64(maximum) else { throw PetMarketError.invalid("小铺内容超过大小限制") }
        var result = Data()
        for try await byte in bytes {
            try Task.checkCancellation()
            guard result.count < maximum else { throw PetMarketError.invalid("小铺内容超过大小限制") }
            result.append(byte)
        }
        guard success else {
            let object = try? JSONSerialization.jsonObject(with: result) as? [String: Any]
            throw PetMarketError.server(object?["error"] as? String ?? "小铺请求失败（\(http.statusCode)）")
        }
        return result
    }

    func catalog() async throws -> PetMarketCatalog {
        let result = try PetMarketJSON.decoder().decode(PetMarketCatalog.self,
            from: await data("/api/pet-market/catalog", authenticated: licenses.isActivated))
        guard result.products.count <= 2000 else { throw PetMarketError.invalid("商品目录过大") }
        for product in result.products {
            guard PetMarketJSON.validPart(product.id), PetMarketJSON.validPart(product.activeVersion, maximum: 40),
                  product.previewFiles.count <= 5, product.currency == "CNY", product.priceFen >= 0,
                  (1...80).contains(product.assetCount), ["paid", "free"].contains(product.chargingMode) else {
                throw PetMarketError.invalid("商品目录无效")
            }
        }
        return result
    }

    func orders() async throws -> [PetMarketOrder] {
        var all: [PetMarketOrder] = [], seen = Set<String>(), cursors = Set<String>()
        var path = "/api/pet-market/orders"
        while true {
            let page = try PetMarketJSON.decoder().decode(PetMarketOrderPage.self, from: await data(path, authenticated: true))
            guard page.orders.count <= 100, all.count + page.orders.count <= 10_000 else { throw PetMarketError.invalid("订单目录过大") }
            serverOffset = page.serverTime.timeIntervalSinceNow
            for order in page.orders {
                try validate(order)
                guard seen.insert(order.id).inserted else { throw PetMarketError.invalid("订单分页重复") }
                all.append(order)
            }
            guard let cursor = page.nextCursor, !cursor.isEmpty else { break }
            guard cursor.count <= 512, cursor.range(of: "^[A-Za-z0-9_-]+$", options: .regularExpression) != nil,
                  !page.orders.isEmpty, cursors.insert(cursor).inserted else { throw PetMarketError.invalid("订单分页无效") }
            path = "/api/pet-market/orders?cursor=\(cursor)"
        }
        return all
    }

    private func validate(_ order: PetMarketOrder) throws {
        guard UUID(uuidString: order.id) != nil, PetMarketJSON.validPart(order.productId),
              ["paid", "pending_payment", "canceled", "expired"].contains(order.status),
              order.currency == "CNY", order.amountFen >= 0,
              order.paymentReference.range(of: "^[0-9]{3}[A-Za-z]{2}$", options: .regularExpression) != nil,
              order.expiresAt >= order.createdAt else { throw PetMarketError.invalid("订单内容无效") }
    }

    func createOrder(_ id: String) async throws -> PetMarketOrder {
        guard PetMarketJSON.validPart(id) else { throw PetMarketError.invalid("商品标识无效") }
        let result = try PetMarketJSON.decoder().decode(PetMarketOrderResponse.self,
            from: await data("/api/pet-market/orders", method: "POST", body: ["productId": id], authenticated: true))
        try validate(result.order)
        serverOffset = result.serverTime.timeIntervalSinceNow
        return result.order
    }

    func cancelOrder(_ id: String) async throws {
        guard UUID(uuidString: id) != nil else { throw PetMarketError.invalid("订单标识无效") }
        _ = try await data("/api/pet-market/orders/\(id)/cancel", method: "POST", body: [:], authenticated: true)
    }

    func claim(_ id: String) async throws {
        guard PetMarketJSON.validPart(id) else { throw PetMarketError.invalid("商品标识无效") }
        _ = try await data("/api/pet-market/products/\(id)/claim", method: "POST", body: [:], authenticated: true)
    }

    func verify(_ envelope: PetMarketSignedEnvelope, kind: String) throws -> PetMarketGrant {
        guard licenses.isActivated, envelope.signatureAlgorithm == "ed25519",
              envelope.signedPayload.count <= 64 * 1024, let bytes = Data(base64Encoded: envelope.signedPayload),
              let signature = Data(base64Encoded: envelope.signature), signature.count == 64,
              Self.hash(bytes) == envelope.sha256.lowercased(), publicKey.isValidSignature(signature, for: bytes) else {
            throw PetMarketError.invalid("小铺授权签名无效")
        }
        // Decode only the authenticated bytes, never the unsigned payload mirror.
        let grant = try PetMarketJSON.decoder().decode(PetMarketGrant.self, from: bytes)
        guard grant.schemaVersion == 1, grant.kind == kind, !grant.accountId.isEmpty,
              grant.licenseId == licenses.marketLicenseId, grant.installationId == licenses.installationId,
              grant.expiresAt > serverNow, grant.issuedAt <= serverNow.addingTimeInterval(120),
              grant.expiresAt > grant.issuedAt, grant.expiresAt.timeIntervalSince(grant.issuedAt) <= 15 * 60,
              PetMarketJSON.validPart(grant.productId), PetMarketJSON.validPart(grant.version, maximum: 40),
              PetMarketJSON.validHash(grant.manifestSha256), licenses.acceptMarketAccount(grant.accountId) else {
            throw PetMarketError.invalid("小铺授权已到期或不属于当前设备")
        }
        return grant
    }

    func authorizeUse(_ installation: PetMarketInstallation) async throws -> PetMarketInstallation {
        guard PetMarketJSON.validPart(installation.productId) else { throw PetMarketError.invalid("商品标识无效") }
        let envelope = try PetMarketJSON.decoder().decode(PetMarketSignedEnvelope.self, from: await data(
            "/api/pet-market/products/\(installation.productId)/authorize-use", method: "POST", body: [:], authenticated: true))
        let grant = try verify(envelope, kind: "pet-market-use-grant")
        guard grant.productId == installation.productId, grant.version == installation.version,
              grant.manifestSha256 == installation.manifestSha256,
              PetMarketInstaller.validInstallation(installation, root: storage) else {
            throw PetMarketError.invalid("本机桌宠与授权不匹配，请重新安装")
        }
        var updated = installation
        updated.useGrant = envelope
        return updated
    }

    func hasValidStoredUseGrant(_ installation: PetMarketInstallation) -> Bool {
        guard let envelope = installation.useGrant,
              let grant = try? verify(envelope, kind: "pet-market-use-grant") else { return false }
        return grant.productId == installation.productId && grant.version == installation.version
            && grant.manifestSha256 == installation.manifestSha256
            && PetMarketInstaller.validInstallation(installation, root: storage)
    }

    func install(_ productId: String, title: String) async throws -> PetMarketInstallation {
        guard PetMarketJSON.validPart(productId) else { throw PetMarketError.invalid("商品标识无效") }
        let envelope = try PetMarketJSON.decoder().decode(PetMarketSignedEnvelope.self,
            from: await data("/api/pet-market/products/\(productId)/download-descriptor", authenticated: true))
        let descriptor = try verify(envelope, kind: "pet-market-download")
        let expected = "/api/pet-market/products/\(productId)/versions/\(descriptor.version)/download"
        guard descriptor.productId == productId, descriptor.downloadPath == expected,
              let size = descriptor.sizeBytes, (1...128 * 1024 * 1024).contains(size),
              let hash = descriptor.sha256, PetMarketJSON.validHash(hash) else { throw PetMarketError.invalid("下载授权内容无效") }
        let bytes = try await data(expected, authenticated: true, limit: size)
        guard bytes.count == size, Self.hash(bytes) == hash.lowercased() else { throw PetMarketError.invalid("桌宠包校验失败") }
        let root = storage
        let work = Task.detached(priority: .utility) {
            try PetMarketInstaller.install(bytes, descriptor: descriptor, title: title, root: root)
        }
        let installation = try await withTaskCancellationHandler(operation: {
            try await work.value
        }, onCancel: { work.cancel() })
        do {
            try Task.checkCancellation()
            return try await authorizeUse(installation)
        }
        catch {
            // This unique directory belongs to this failed install attempt only.
            let destination = URL(fileURLWithPath: installation.directory).standardizedFileURL
            if destination.path.hasPrefix(storage.appendingPathComponent("pets").standardizedFileURL.path + "/") {
                try? FileManager.default.removeItem(at: destination)
            }
            throw error
        }
    }

    func preview(_ product: PetMarketProduct, index: Int) async throws -> Data? {
        guard product.previewFiles.indices.contains(index) else { return nil }
        let file = product.previewFiles[index]
        guard (1...8 * 1024 * 1024).contains(file.sizeBytes), PetMarketJSON.validHash(file.sha256) else {
            throw PetMarketError.invalid("预览清单无效")
        }
        let candidate = file.downloadPath ?? file.url ?? ""
        let path: String
        if candidate.hasPrefix("/"), !candidate.hasPrefix("//") { path = candidate }
        else if let url = URL(string: candidate), url.host == origin.host, url.scheme == origin.scheme, url.port == origin.port {
            path = url.path + (url.query.map { "?\($0)" } ?? "")
        } else { throw PetMarketError.invalid("预览地址无效") }
        guard let components = URLComponents(string: path),
              components.path == "/api/pet-market/products/\(product.id)/versions/\(product.activeVersion)/preview",
              components.queryItems?.count == 1, components.queryItems?.first?.name == "file",
              components.queryItems?.first?.value == file.path else { throw PetMarketError.invalid("预览路径无效") }
        let result = try await data(path, limit: file.sizeBytes)
        guard result.count == file.sizeBytes, Self.hash(result) == file.sha256.lowercased() else { throw PetMarketError.invalid("预览校验失败") }
        return result
    }

    nonisolated static func hash(_ data: Data) -> String { SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined() }
}
