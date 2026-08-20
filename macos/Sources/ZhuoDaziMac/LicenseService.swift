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
    let deviceCount: Int?
    let alreadyActivated: Bool?
}

struct TrialStatus {
    let allowed: Bool
    let remainingSeconds: Int
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
            return "请输入 6 位有效激活码"
        case .server(let message):
            return message
        case .invalidResponse:
            return "激活服务返回的数据无效"
        case .keychain:
            return "无法安全保存本机授权信息"
        }
    }
}

final class LicenseService {
    static let serviceBaseURL = DeskPetApi.baseURL
    private static let activationURL = DeskPetApi.activate
    private static let trialURL = DeskPetApi.trial
    private let service = Bundle.main.bundleIdentifier ?? "com.zhuodazi.desktop-pet"
    private let account = "device-license"
    private var record: LicenseRecord
    private var trialActive = false
    private var remainingTrialSeconds = 0
    private var trialCheckedAt = Date.distantPast
    private(set) var deviceCount = 1
    private(set) var alreadyActivated = false

    var isActivated: Bool {
        guard let identifier = record.licenseId else { return false }
        return UUID(uuidString: identifier) != nil
    }

    var isTrialActive: Bool { !isActivated && trialActive && remainingTrialSecondsNow > 0 }
    var hasPremiumAccess: Bool { isActivated || isTrialActive }
    var remainingTrialSecondsNow: Int {
        guard trialActive, !isActivated else { return 0 }
        let elapsed = Int(Date().timeIntervalSince(trialCheckedAt))
        return max(0, remainingTrialSeconds - elapsed)
    }

    var summary: String {
        guard let identifier = record.licenseId, isActivated else { return "此设备尚未绑定" }
        return "已完成绑定 - \(identifier.suffix(8))"
    }

    var activationSuccessMessage: String {
        deviceCount >= 2
            ? "这台也连上了，搭子码和另一台是同一对。"
            : "这组码也可以填到另一台电脑或手机。"
    }

    var interactionCacheKey: String {
        record.licenseId?.lowercased() ?? "pending-\(record.installationId.lowercased())"
    }

    var installationId: String {
        record.installationId
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
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
        request.setValue(Self.architecture, forHTTPHeaderField: "X-DeskPet-Architecture")
        request.httpBody = try JSONSerialization.data(withJSONObject: payload)

        let (data, response) = try await URLSession.shared.data(for: request)
        guard data.count <= 32 * 1024, let http = response as? HTTPURLResponse else {
            throw LicenseError.invalidResponse
        }
        guard (200..<300).contains(http.statusCode) else {
            throw LicenseError.server(Self.readError(data) ?? "激活码验证失败，请检查后重试")
        }
        guard let result = try? JSONDecoder().decode(ActivationResponse.self, from: data),
              UUID(uuidString: result.licenseId) != nil else {
            throw LicenseError.invalidResponse
        }
        record = candidate
        record.licenseId = result.licenseId
        record.activatedAt = result.activatedAt
        if let count = result.deviceCount, (1...2).contains(count) {
            deviceCount = count
        } else {
            deviceCount = 1
        }
        alreadyActivated = result.alreadyActivated ?? false
        try save()
    }

    func checkTrial() async throws -> TrialStatus {
        let payload: [String: String] = [
            "installationId": record.installationId,
            "credential": record.credential,
            "appVersion": AppVersion.current
        ]
        var request = URLRequest(url: Self.trialURL)
        request.httpMethod = "POST"
        request.timeoutInterval = 25
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("ZhuoDazi/\(AppVersion.current)", forHTTPHeaderField: "User-Agent")
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
        request.setValue(Self.architecture, forHTTPHeaderField: "X-DeskPet-Architecture")
        request.httpBody = try JSONSerialization.data(withJSONObject: payload)

        let (data, response) = try await URLSession.shared.data(for: request)
        guard data.count <= 32 * 1024, let http = response as? HTTPURLResponse else {
            throw LicenseError.invalidResponse
        }
        guard (200..<300).contains(http.statusCode) else {
            throw LicenseError.server(Self.readError(data) ?? "试用时间校验失败，请稍后重试")
        }
        guard let result = try? JSONDecoder().decode(TrialResponse.self, from: data),
              (0...86400).contains(result.remainingSeconds) else {
            throw LicenseError.invalidResponse
        }
        trialActive = result.allowed && result.remainingSeconds > 0
        remainingTrialSeconds = trialActive ? result.remainingSeconds : 0
        trialCheckedAt = Date()
        return TrialStatus(
            allowed: trialActive,
            remainingSeconds: remainingTrialSecondsNow
        )
    }

    func endTrial() {
        trialActive = false
        remainingTrialSeconds = 0
    }

    func authorize(_ request: inout URLRequest) throws {
        if isActivated, let licenseId = record.licenseId {
            request.setValue("Bearer \(licenseId).\(record.credential)", forHTTPHeaderField: "Authorization")
        } else if trialActive {
            request.setValue("Trial \(record.installationId).\(record.credential)", forHTTPHeaderField: "Authorization")
        } else {
            throw LicenseError.inactive
        }
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

    private static var architecture: String {
#if arch(arm64)
        return "arm64"
#else
        return "x86_64"
#endif
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

    private struct TrialResponse: Decodable {
        let allowed: Bool
        let remainingSeconds: Int
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
