import Foundation

enum PetMarketError: LocalizedError {
    case invalid(String)
    case server(String)
    var errorDescription: String? {
        switch self { case .invalid(let text), .server(let text): return text }
    }
}

struct PetMarketPayment: Codable, Equatable {
    var enabled: Bool
    var available: Bool
    var method: String
    var recipientName: String
    var recipientAccount: String
    var qrCodeDataUrl: String
    var instructions: String
    var unavailableReason: String
    var listenerOnline: Bool
    var canOrder: Bool { enabled && available && listenerOnline }
}

struct PetMarketPreview: Codable, Equatable {
    var path: String
    var sha256: String
    var sizeBytes: Int
    var downloadPath: String?
    var url: String?
}

struct PetMarketProduct: Decodable, Identifiable {
    var id: String
    var title: String
    var description: String
    var chargingMode: String
    var priceFen: Int
    var currency: String
    var isDefault: Bool
    var isFeatured: Bool
    var tags: [String]
    var sortOrder: Int
    var activeVersion: String
    var assetCount: Int
    var previewFiles: [PetMarketPreview]
    var owned: Bool
    var ownedVersion: String?
    var entitlementVersion: String?
    var isFree: Bool { chargingMode == "free" }
    var priceLabel: String { isFree ? "免费" : String(format: "¥%.2f", Double(priceFen) / 100) }
}

struct PetMarketCatalog: Decodable {
    var products: [PetMarketProduct]
    var payment: PetMarketPayment?
    var defaultProductId: String?
}

struct PetMarketOrder: Codable, Equatable, Identifiable {
    var id: String
    var productId: String
    var productTitle: String?
    var productVersion: String
    var status: String
    var amountFen: Int
    var currency: String
    var paymentReference: String
    var payment: PetMarketPayment?
    var createdAt: Date
    var expiresAt: Date
    var paidAt: Date?
    var canceledAt: Date?
    var statusLabel: String {
        switch status { case "paid": return "已付款"; case "pending_payment": return "待付款"
        case "canceled": return "已取消"; case "expired": return "已过期"; default: return "未知状态" }
    }
}

struct PetMarketOrderResponse: Decodable { var order: PetMarketOrder; var serverTime: Date; var reused: Bool? }
struct PetMarketOrderPage: Decodable { var orders: [PetMarketOrder]; var serverTime: Date; var nextCursor: String? }
struct PetMarketSignedEnvelope: Codable, Equatable {
    var signedPayload: String
    var sha256: String
    var signatureAlgorithm: String
    var signature: String
}
struct PetMarketGrant: Decodable {
    var schemaVersion: Int
    var kind: String
    var accountId: String
    var licenseId: String
    var installationId: String
    var productId: String
    var version: String
    var manifestSha256: String
    var issuedAt: Date
    var expiresAt: Date
    var sha256: String?
    var sizeBytes: Int?
    var downloadPath: String?
}
struct PetMarketManifest: Decodable {
    struct File: Decodable { var path: String; var sha256: String; var sizeBytes: Int }
    var schemaVersion: Int
    var productId: String
    var version: String
    var title: String
    var files: [File]
}
struct PetMarketInstallation: Codable, Equatable, Identifiable {
    var productId: String
    var title: String
    var version: String
    var directory: String
    var files: [String]
    var manifestSha256: String
    var installedAt: Date
    var useGrant: PetMarketSignedEnvelope?
    var id: String { productId }
}

enum PetMarketJSON {
    static func decoder() -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let text = try decoder.singleValueContainer().decode(String.self)
            let parser = ISO8601DateFormatter()
            parser.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            if let date = parser.date(from: text) { return date }
            parser.formatOptions = [.withInternetDateTime]
            guard let date = parser.date(from: text) else { throw PetMarketError.invalid("日期格式无效") }
            return date
        }
        return decoder
    }
    static func validPart(_ value: String, maximum: Int = 64) -> Bool {
        !value.isEmpty && value.utf8.count <= maximum && value != "." && value != ".."
            && value.range(of: "^[A-Za-z0-9][A-Za-z0-9._-]*$", options: .regularExpression) != nil
    }
    static func validHash(_ value: String) -> Bool {
        value.range(of: "^[0-9a-fA-F]{64}$", options: .regularExpression) != nil
    }
}
