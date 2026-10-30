import Foundation
import ZhuoDaziCore

private final class UninstallNoRedirectSessionDelegate: NSObject, URLSessionTaskDelegate {
    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest,
        completionHandler: @escaping (URLRequest?) -> Void
    ) {
        completionHandler(nil)
    }
}

enum UninstallReason: String, CaseIterable {
    case notOftenUsed = "not_often_used"
    case resourceUsage = "resource_usage"
    case experienceIssue = "experience_issue"
    case temporaryPause = "temporary_pause"
    case other

    var title: String {
        switch self {
        case .notOftenUsed: return "不常用"
        case .resourceUsage: return "占用资源"
        case .experienceIssue: return "体验问题"
        case .temporaryPause: return "暂时不用"
        case .other: return "其他"
        }
    }
}

struct UninstallIntentReceipt: Decodable {
    let eventId: String
    let completionToken: String
    let duplicate: Bool
}

private struct InstallEventResponse: Decodable {
    let ok: Bool
    let eventId: String
    let duplicate: Bool
    let completionToken: String?
}

private struct InstallBatchRecord: Codable {
    var id: String
    var createdAt: Date
    var installedStartEventId: String
}

enum UninstallFeedbackError: LocalizedError {
    case invalidComment
    case invalidCommentReason
    case invalidResponse
    case packagedApplicationRequired
    case helperUnavailable
    case server(String)

    var errorDescription: String? {
        switch self {
        case .invalidComment: return "补充说明最多 500 字。"
        case .invalidCommentReason: return "补充说明仅在选择“其他”原因时填写。"
        case .invalidResponse: return "卸载反馈服务返回的数据无效，请稍后重试。"
        case .packagedApplicationRequired: return "当前不是已打包的桌搭子 App，请在 Finder 中手动移到废纸篓。"
        case .helperUnavailable: return "卸载助手缺失或不可执行，请在 Finder 中手动移到废纸篓。"
        case .server(let message): return message
        }
    }
}

final class UninstallFeedbackService {
    private let licenses: LicenseService
    private let defaults: UserDefaults
    private let batchKey = ControlledUninstallContract.installBatchDefaultsKey
    private let reportedBatchKey = ControlledUninstallContract.reportedBatchDefaultsKey
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()
    private let session: URLSession

    init(licenses: LicenseService, defaults: UserDefaults = .standard) {
        self.licenses = licenses
        self.defaults = defaults
        let configuration = URLSessionConfiguration.ephemeral
        configuration.httpCookieStorage = nil
        configuration.urlCache = nil
        session = URLSession(
            configuration: configuration,
            delegate: UninstallNoRedirectSessionDelegate(),
            delegateQueue: nil
        )
        encoder.dateEncodingStrategy = .iso8601
        decoder.dateDecodingStrategy = .iso8601
        _ = installBatch
    }

    func reportInstalledStartIfNeeded() async {
        let batch = installBatch
        guard defaults.string(forKey: reportedBatchKey) != batch.id else { return }
        do {
            let response = try await sendIdentityEvent(
                type: "installed_start",
                eventId: batch.installedStartEventId,
                batch: batch,
                reason: nil,
                comment: nil
            )
            guard response.eventId.caseInsensitiveCompare(batch.installedStartEventId) == .orderedSame else { return }
            defaults.set(batch.id, forKey: reportedBatchKey)
        } catch {
            // Startup reporting is best effort and retries on a later launch.
        }
    }

    var currentInstallBatchId: String { installBatch.id }

    func reportIntent(
        reason: UninstallReason?,
        comment: String?,
        installBatchId: String
    ) async throws -> UninstallIntentReceipt {
        let cleanComment = comment?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard cleanComment.unicodeScalars.count <= 500 else { throw UninstallFeedbackError.invalidComment }
        guard cleanComment.isEmpty || reason == .other else { throw UninstallFeedbackError.invalidCommentReason }
        let batch = installBatch
        guard batch.id.caseInsensitiveCompare(installBatchId) == .orderedSame else {
            throw UninstallFeedbackError.invalidResponse
        }
        let eventId = UUID().uuidString.lowercased()
        let response = try await sendIdentityEvent(
            type: "uninstall_intent",
            eventId: eventId,
            batch: batch,
            reason: reason,
            comment: cleanComment.isEmpty ? nil : cleanComment
        )
        guard response.eventId.caseInsensitiveCompare(eventId) == .orderedSame,
              let token = response.completionToken,
              ControlledUninstallContract.isValidCompletionToken(token) else {
            throw UninstallFeedbackError.invalidResponse
        }
        return UninstallIntentReceipt(eventId: response.eventId, completionToken: token, duplicate: response.duplicate)
    }

    func launchControlledUninstall(receipt: UninstallIntentReceipt?, installBatchId: String) throws {
        guard UUID(uuidString: installBatchId) != nil else {
            throw UninstallFeedbackError.invalidResponse
        }
        let bundleURL = Bundle.main.bundleURL.standardizedFileURL
        guard Bundle.main.bundleIdentifier == ControlledUninstallContract.applicationBundleIdentifier,
              bundleURL.pathExtension.lowercased() == "app",
              bundleURL.resolvingSymlinksInPath().standardizedFileURL.path == bundleURL.path else {
            throw UninstallFeedbackError.packagedApplicationRequired
        }
        guard installBatch.id.caseInsensitiveCompare(installBatchId) == .orderedSame else {
            throw UninstallFeedbackError.invalidResponse
        }

        let helperURL = bundleURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("Helpers", isDirectory: true)
            .appendingPathComponent(ControlledUninstallContract.helperExecutableName, isDirectory: false)
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: helperURL.path, isDirectory: &isDirectory),
              !isDirectory.boolValue,
              helperURL.resolvingSymlinksInPath().standardizedFileURL.path == helperURL.path,
              FileManager.default.isExecutableFile(atPath: helperURL.path) else {
            throw UninstallFeedbackError.helperUnavailable
        }

        var receiptURL: URL?
        do {
            let writtenReceiptURL = try writeControlledReceipt(receipt, installBatchId: installBatchId)
            receiptURL = writtenReceiptURL
            let process = Process()
            process.executableURL = helperURL
            process.arguments = [
                "--parent-pid", String(ProcessInfo.processInfo.processIdentifier),
                "--receipt", writtenReceiptURL.path
            ]
            process.standardInput = FileHandle.nullDevice
            process.standardOutput = FileHandle.nullDevice
            process.standardError = FileHandle.nullDevice
            try process.run()
        } catch {
            if let directory = receiptURL?.deletingLastPathComponent() {
                try? FileManager.default.removeItem(at: directory)
            }
            throw error
        }
    }

    private func writeControlledReceipt(
        _ receipt: UninstallIntentReceipt?,
        installBatchId: String
    ) throws -> URL {
        let completion = receipt.map {
            ControlledUninstallReceipt(
                completionEventId: UUID().uuidString.lowercased(),
                intentEventId: $0.eventId,
                completionToken: $0.completionToken
            )
        }
        let launchReceipt = ControlledUninstallLaunchReceipt(
            installBatchId: installBatchId,
            completion: completion
        )
        guard launchReceipt.isValid() else { throw UninstallFeedbackError.invalidResponse }

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(
            "ZhuoDazi-Uninstall-\(UUID().uuidString.lowercased())",
            isDirectory: true
        )
        try FileManager.default.createDirectory(
            at: directory,
            withIntermediateDirectories: false,
            attributes: [.posixPermissions: 0o700]
        )
        do {
            let url = directory.appendingPathComponent("completion.json", isDirectory: false)
            let receiptEncoder = JSONEncoder()
            receiptEncoder.dateEncodingStrategy = .iso8601
            try receiptEncoder.encode(launchReceipt).write(to: url, options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
            return url
        } catch {
            try? FileManager.default.removeItem(at: directory)
            throw error
        }
    }

    private var installBatch: InstallBatchRecord {
        if let data = defaults.data(forKey: batchKey),
           let value = try? decoder.decode(InstallBatchRecord.self, from: data),
           UUID(uuidString: value.id) != nil,
           UUID(uuidString: value.installedStartEventId) != nil {
            return value
        }
        let value = InstallBatchRecord(
            id: UUID().uuidString.lowercased(),
            createdAt: Date(),
            installedStartEventId: UUID().uuidString.lowercased()
        )
        if let data = try? encoder.encode(value) { defaults.set(data, forKey: batchKey) }
        return value
    }

    private func sendIdentityEvent(
        type: String,
        eventId: String,
        batch: InstallBatchRecord,
        reason: UninstallReason?,
        comment: String?
    ) async throws -> InstallEventResponse {
        var body: [String: Any] = [
            "schemaVersion": 1,
            "eventId": eventId,
            "eventType": type,
            "installationId": licenses.installationId,
            "credential": licenses.installationCredential,
            "installBatchId": batch.id,
            "installBatchCreatedAt": Self.timestamp(batch.createdAt),
            "platform": "macos",
            "appVersion": AppVersion.current,
            "occurredAt": Self.timestamp(Date())
        ]
        if let reason { body["reason"] = reason.rawValue }
        if let comment { body["comment"] = comment }
        return try await send(body)
    }

    private func send(_ body: [String: Any]) async throws -> InstallEventResponse {
        var request = URLRequest(url: DeskPetApi.deviceInstallEvents)
        request.httpMethod = "POST"
        request.timeoutInterval = 8
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("ZhuoDazi/\(AppVersion.current)", forHTTPHeaderField: "User-Agent")
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        let (data, response) = try await session.data(for: request)
        guard data.count <= 32 * 1024, let http = response as? HTTPURLResponse else {
            throw UninstallFeedbackError.invalidResponse
        }
        guard (200..<300).contains(http.statusCode) else {
            let message = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["message"] as? String
            throw UninstallFeedbackError.server(message ?? "卸载反馈暂时无法提交，请检查网络后重试。")
        }
        guard let result = try? JSONDecoder().decode(InstallEventResponse.self, from: data), result.ok else {
            throw UninstallFeedbackError.invalidResponse
        }
        return result
    }

    private static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
