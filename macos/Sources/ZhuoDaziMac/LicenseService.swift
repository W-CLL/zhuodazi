import Foundation
import Security

private struct LicenseRecord: Codable {
    var version = 1
    var installationId: String
    var credential: String
    var licenseId: String?
    var activatedAt: String?
}

private struct ActivationResponse: Decodable {
    let licenseId: String
    let activatedAt: String?
}

enum LicenseError: LocalizedError {
    case inactive
    case invalidActivationCode
    case server(String)
    case invalidResponse
    case keychain(OSStatus)

    var errorDescription: String? {
        switch self {
        case .inactive:
            return "此设备尚未完成绑定"
        case .invalidActivationCode:
            return "请输入 6 位有效邀请码"
        case .server(let message):
            return message
        case .invalidResponse:
            return "邀请服务返回的数据无效"
        case .keychain:
            return "无法安全保存本机授权信息"
        }
    }
}

final class LicenseService {
    private static let activationURL = URL(string: "https://8.134.130.155/api/activate")!
    private let service = Bundle.main.bundleIdentifier ?? "com.zhuodazi.desktop-pet"
    private let account = "device-license"
    private var record: LicenseRecord

    var isActivated: Bool {
        guard let identifier = record.licenseId else { return false }
        return UUID(uuidString: identifier) != nil
    }

    var summary: String {
        guard let identifier = record.licenseId, isActivated else { return "此设备尚未绑定" }
        return "已完成绑定 - \(identifier.suffix(8))"
    }

    init() throws {
        if let stored = try Self.readKeychain(service: service, account: account),
           let decoded = try? JSONDecoder().decode(LicenseRecord.self, from: stored),
           Self.isValid(decoded) {
            record = decoded
        } else {
            record = Self.createPendingRecord()
            try save()
        }
    }

    func activate(_ activationCode: String, replacingExisting: Bool = false) async throws {
        let code = activationCode.uppercased().unicodeScalars
            .filter { CharacterSet.alphanumerics.contains($0) }
            .map(String.init)
            .joined()
        guard code.count == 6 else { throw LicenseError.invalidActivationCode }

        let candidate = replacingExisting ? Self.createPendingRecord() : record
        let payload: [String: String] = [
            "code": code,
            "installationId": candidate.installationId,
            "credential": candidate.credential,
            "appVersion": AppVersion.current
        ]
        var request = URLRequest(url: Self.activationURL)
        request.httpMethod = "POST"
        request.timeoutInterval = 25
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("ZhuoDazi/\(AppVersion.current)", forHTTPHeaderField: "User-Agent")
        request.httpBody = try JSONSerialization.data(withJSONObject: payload)

        let (data, response) = try await URLSession.shared.data(for: request)
        guard data.count <= 32 * 1024, let http = response as? HTTPURLResponse else {
            throw LicenseError.invalidResponse
        }
        guard (200..<300).contains(http.statusCode) else {
            throw LicenseError.server(Self.readError(data) ?? "邀请码验证失败，请检查后重试")
        }
        guard let result = try? JSONDecoder().decode(ActivationResponse.self, from: data),
              UUID(uuidString: result.licenseId) != nil else {
            throw LicenseError.invalidResponse
        }
        record = candidate
        record.licenseId = result.licenseId
        record.activatedAt = result.activatedAt
        try save()
    }

    func authorize(_ request: inout URLRequest) throws {
        guard isActivated, let licenseId = record.licenseId else { throw LicenseError.inactive }
        request.setValue("Bearer \(licenseId).\(record.credential)", forHTTPHeaderField: "Authorization")
        request.setValue(AppVersion.current, forHTTPHeaderField: "X-DeskPet-Version")
        request.setValue("ZhuoDazi/\(AppVersion.current)", forHTTPHeaderField: "User-Agent")
    }

    private func save() throws {
        let data = try JSONEncoder().encode(record)
        try Self.writeKeychain(data, service: service, account: account)
    }

    private static func createPendingRecord() -> LicenseRecord {
        LicenseRecord(
            installationId: randomBytes(count: 16).map { String(format: "%02x", $0) }.joined(),
            credential: base64URLEncoded(randomBytes(count: 32))
        )
    }

    private static func isValid(_ record: LicenseRecord) -> Bool {
        guard record.version == 1,
              record.installationId.count == 32,
              record.installationId.allSatisfy({ $0.isHexDigit }),
              record.credential.count == 43,
              record.credential.allSatisfy({ $0.isLetter || $0.isNumber || $0 == "-" || $0 == "_" }) else {
            return false
        }
        return record.licenseId == nil || UUID(uuidString: record.licenseId!) != nil
    }

    private static func randomBytes(count: Int) -> [UInt8] {
        var bytes = [UInt8](repeating: 0, count: count)
        let status = SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes)
        precondition(status == errSecSuccess, "Secure random source unavailable")
        return bytes
    }

    private static func base64URLEncoded(_ bytes: [UInt8]) -> String {
        Data(bytes).base64EncodedString()
            .replacingOccurrences(of: "=", with: "")
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
    }

    private static func readKeychain(service: String, account: String) throws -> Data? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw LicenseError.keychain(status) }
        return result as? Data
    }

    private static func writeKeychain(_ data: Data, service: String, account: String) throws {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
        let update = SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if update == errSecSuccess { return }
        guard update == errSecItemNotFound else { throw LicenseError.keychain(update) }
        var create = query
        create[kSecValueData as String] = data
        let inserted = SecItemAdd(create as CFDictionary, nil)
        guard inserted == errSecSuccess else { throw LicenseError.keychain(inserted) }
    }

    private static func readError(_ data: Data) -> String? {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let error = object["error"] as? String,
              !error.isEmpty else { return nil }
        return error
    }
}
