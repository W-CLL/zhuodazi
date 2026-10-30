import Foundation

public enum ControlledUninstallContract {
    public static let applicationBundleIdentifier = "com.zhuodazi.desktop-pet"
    public static let helperExecutableName = "ZhuoDaziUninstallHelper"
    public static let installBatchDefaultsKey = "com.zhuodazi.install-batch.v1"
    public static let reportedBatchDefaultsKey = "com.zhuodazi.install-batch.reported.v1"
    public static let completionEndpoint = URL(
        string: "https://in.desktoppet.online/api/device/install-events"
    )!

    public static func isValidCompletionToken(_ value: String) -> Bool {
        (40...80).contains(value.utf8.count) && value.utf8.allSatisfy { byte in
            (48...57).contains(byte)
                || (65...90).contains(byte)
                || (97...122).contains(byte)
                || byte == 45
                || byte == 95
        }
    }
}

public struct ControlledUninstallReceipt: Codable, Equatable {
    public let schemaVersion: Int
    public let completionEventId: String
    public let intentEventId: String
    public let completionToken: String
    public let issuedAt: Date

    public init(
        completionEventId: String,
        intentEventId: String,
        completionToken: String,
        issuedAt: Date = Date()
    ) {
        schemaVersion = 1
        self.completionEventId = completionEventId
        self.intentEventId = intentEventId
        self.completionToken = completionToken
        self.issuedAt = issuedAt
    }

    public func isValid(now: Date = Date()) -> Bool {
        schemaVersion == 1
            && UUID(uuidString: completionEventId) != nil
            && UUID(uuidString: intentEventId) != nil
            && ControlledUninstallContract.isValidCompletionToken(completionToken)
            && issuedAt <= now.addingTimeInterval(5 * 60)
            && issuedAt >= now.addingTimeInterval(-2 * 60 * 60)
    }
}

public struct ControlledUninstallLaunchReceipt: Codable, Equatable {
    public let schemaVersion: Int
    public let installBatchId: String
    public let completion: ControlledUninstallReceipt?
    public let issuedAt: Date

    public init(
        installBatchId: String,
        completion: ControlledUninstallReceipt?,
        issuedAt: Date = Date()
    ) {
        schemaVersion = 1
        self.installBatchId = installBatchId
        self.completion = completion
        self.issuedAt = issuedAt
    }

    public func isValid(now: Date = Date()) -> Bool {
        schemaVersion == 1
            && UUID(uuidString: installBatchId) != nil
            && issuedAt <= now.addingTimeInterval(5 * 60)
            && issuedAt >= now.addingTimeInterval(-2 * 60 * 60)
            && (completion?.isValid(now: now) ?? true)
    }
}
