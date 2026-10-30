import Darwin
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

private enum HelperError: LocalizedError {
    case invalidArguments
    case invalidApplication
    case invalidReceipt
    case applicationStillRunning
    case trashFailed

    var errorDescription: String? {
        switch self {
        case .invalidArguments: return "Invalid uninstall helper arguments."
        case .invalidApplication: return "The uninstall helper is not inside the expected application bundle."
        case .invalidReceipt: return "The uninstall completion receipt is invalid."
        case .applicationStillRunning: return "The application did not exit before the uninstall timeout."
        case .trashFailed: return "The application could not be moved to the Trash."
        }
    }
}

@main
struct ZhuoDaziUninstallHelper {
    private static let completionSession: URLSession = {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.httpCookieStorage = nil
        configuration.urlCache = nil
        return URLSession(
            configuration: configuration,
            delegate: UninstallNoRedirectSessionDelegate(),
            delegateQueue: nil
        )
    }()

    static func main() async {
        do {
            try await run()
        } catch {
            if let data = "ZhuoDazi uninstall helper: \(error.localizedDescription)\n".data(using: .utf8) {
                try? FileHandle.standardError.write(contentsOf: data)
            }
            Darwin.exit(EXIT_FAILURE)
        }
    }

    private static func run() async throws {
        let arguments = try parsedArguments()
        let application = try containingApplication()
        let receipt = try loadReceipt(from: arguments.receiptURL)
        defer { removeReceiptDirectory(receipt) }

        try await waitForExit(processId: arguments.parentProcessId)
        guard let currentIdentifier = resourceIdentifier(at: application.url),
              currentIdentifier.isEqual(application.resourceIdentifier),
              applicationHasExpectedBundleIdentifier(at: application.url) else {
            throw HelperError.invalidApplication
        }

        var trashedURL: NSURL?
        try FileManager.default.trashItem(at: application.url, resultingItemURL: &trashedURL)
        guard !FileManager.default.fileExists(atPath: application.url.path),
              let movedURL = trashedURL as URL?,
              let movedIdentifier = resourceIdentifier(at: movedURL),
              movedIdentifier.isEqual(application.resourceIdentifier) else {
            throw HelperError.trashFailed
        }

        clearInstallBatch(ifMatching: receipt.value.installBatchId)
        if let completion = receipt.value.completion {
            await reportCompletion(completion)
        }
    }

    private struct Arguments {
        let parentProcessId: pid_t
        let receiptURL: URL
    }

    private struct ApplicationTarget {
        let url: URL
        let resourceIdentifier: NSObject
    }

    private struct ValidatedReceipt {
        let value: ControlledUninstallLaunchReceipt
        let directoryURL: URL
        let directoryResourceIdentifier: NSObject
    }

    private struct StoredInstallBatch: Decodable {
        let id: String
    }

    private static func parsedArguments() throws -> Arguments {
        let values = Array(CommandLine.arguments.dropFirst())
        guard values.count == 4,
              values[0] == "--parent-pid",
              let processId = Int32(values[1]),
              processId > 1,
              processId != getpid(),
              values[2] == "--receipt",
              values[3].hasPrefix("/") else {
            throw HelperError.invalidArguments
        }
        return Arguments(parentProcessId: processId, receiptURL: URL(fileURLWithPath: values[3]))
    }

    private static func containingApplication() throws -> ApplicationTarget {
        let executableURL = URL(fileURLWithPath: CommandLine.arguments[0])
            .resolvingSymlinksInPath().standardizedFileURL
        let helpersURL = executableURL.deletingLastPathComponent()
        let contentsURL = helpersURL.deletingLastPathComponent()
        let appURL = contentsURL.deletingLastPathComponent()
        guard executableURL.lastPathComponent == ControlledUninstallContract.helperExecutableName,
              helpersURL.lastPathComponent == "Helpers",
              contentsURL.lastPathComponent == "Contents",
              appURL.pathExtension.lowercased() == "app",
              appURL.pathComponents.count > 3 else {
            throw HelperError.invalidApplication
        }

        guard applicationHasExpectedBundleIdentifier(at: appURL) else {
            throw HelperError.invalidApplication
        }
        guard let identifier = resourceIdentifier(at: appURL) else {
            throw HelperError.invalidApplication
        }
        return ApplicationTarget(url: appURL, resourceIdentifier: identifier)
    }

    private static func loadReceipt(from suppliedURL: URL) throws -> ValidatedReceipt {
        let manager = FileManager.default
        let suppliedReceiptURL = suppliedURL.standardizedFileURL
        let suppliedDirectoryURL = suppliedReceiptURL.deletingLastPathComponent()
        let temporaryURL = manager.temporaryDirectory.resolvingSymlinksInPath().standardizedFileURL
        guard suppliedReceiptURL.lastPathComponent == "completion.json",
              isValidReceiptDirectoryName(suppliedDirectoryURL.lastPathComponent),
              !isSymbolicLink(at: suppliedReceiptURL),
              !isSymbolicLink(at: suppliedDirectoryURL) else {
            throw HelperError.invalidReceipt
        }

        let receiptURL = suppliedReceiptURL.resolvingSymlinksInPath().standardizedFileURL
        let directoryURL = suppliedDirectoryURL.resolvingSymlinksInPath().standardizedFileURL
        guard directoryURL.deletingLastPathComponent().path == temporaryURL.path,
              receiptURL.deletingLastPathComponent().path == directoryURL.path,
              receiptURL.lastPathComponent == "completion.json" else {
            throw HelperError.invalidReceipt
        }

        let directoryAttributes = try manager.attributesOfItem(atPath: directoryURL.path)
        let fileAttributes = try manager.attributesOfItem(atPath: receiptURL.path)
        let userId = getuid()
        guard directoryAttributes[.type] as? FileAttributeType == .typeDirectory,
              (directoryAttributes[.ownerAccountID] as? NSNumber)?.uint32Value == userId,
              ((directoryAttributes[.posixPermissions] as? NSNumber)?.intValue ?? -1) & 0o777 == 0o700,
              fileAttributes[.type] as? FileAttributeType == .typeRegular,
              (fileAttributes[.ownerAccountID] as? NSNumber)?.uint32Value == userId,
              ((fileAttributes[.posixPermissions] as? NSNumber)?.intValue ?? -1) & 0o777 == 0o600,
              ((fileAttributes[.size] as? NSNumber)?.intValue ?? Int.max) <= 32 * 1024 else {
            throw HelperError.invalidReceipt
        }

        guard let directoryIdentifier = resourceIdentifier(at: directoryURL),
              let fileIdentifier = resourceIdentifier(at: receiptURL) else {
            throw HelperError.invalidReceipt
        }
        let data = try Data(contentsOf: receiptURL, options: [.mappedIfSafe])
        guard resourceIdentifier(at: directoryURL)?.isEqual(directoryIdentifier) == true,
              resourceIdentifier(at: receiptURL)?.isEqual(fileIdentifier) == true else {
            throw HelperError.invalidReceipt
        }

        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let receipt = try decoder.decode(ControlledUninstallLaunchReceipt.self, from: data)
        guard receipt.isValid() else { throw HelperError.invalidReceipt }
        return ValidatedReceipt(
            value: receipt,
            directoryURL: directoryURL,
            directoryResourceIdentifier: directoryIdentifier
        )
    }

    private static func waitForExit(processId: pid_t) async throws {
        let deadline = Date().addingTimeInterval(30)
        while processExists(processId) && Date() < deadline {
            try await Task.sleep(for: .milliseconds(100))
        }
        guard !processExists(processId) else { throw HelperError.applicationStillRunning }
    }

    private static func processExists(_ processId: pid_t) -> Bool {
        if kill(processId, 0) == 0 { return true }
        return errno != ESRCH
    }

    private static func clearInstallBatch(ifMatching expectedBatchId: String) {
        guard let defaults = UserDefaults(suiteName: ControlledUninstallContract.applicationBundleIdentifier) else { return }
        defaults.synchronize()
        guard let data = defaults.data(forKey: ControlledUninstallContract.installBatchDefaultsKey),
              let current = try? JSONDecoder().decode(StoredInstallBatch.self, from: data),
              current.id.caseInsensitiveCompare(expectedBatchId) == .orderedSame else {
            return
        }
        defaults.removeObject(forKey: ControlledUninstallContract.installBatchDefaultsKey)
        if defaults.string(forKey: ControlledUninstallContract.reportedBatchDefaultsKey)?
            .caseInsensitiveCompare(expectedBatchId) == .orderedSame {
            defaults.removeObject(forKey: ControlledUninstallContract.reportedBatchDefaultsKey)
        }
        defaults.synchronize()
    }

    private static func reportCompletion(_ receipt: ControlledUninstallReceipt) async {
        let body: [String: Any] = [
            "schemaVersion": 1,
            "eventType": "uninstall_completed",
            "eventId": receipt.completionEventId,
            "intentEventId": receipt.intentEventId,
            "completionToken": receipt.completionToken,
            "occurredAt": timestamp(Date())
        ]
        guard let bytes = try? JSONSerialization.data(withJSONObject: body) else { return }

        for attempt in 0..<3 {
            do {
                var request = URLRequest(url: ControlledUninstallContract.completionEndpoint)
                request.httpMethod = "POST"
                request.timeoutInterval = 5
                request.setValue("application/json", forHTTPHeaderField: "Content-Type")
                request.setValue("ZhuoDaziUninstallHelper/1", forHTTPHeaderField: "User-Agent")
                request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
                request.httpBody = bytes
                let (data, response) = try await completionSession.data(for: request)
                guard data.count <= 32 * 1024,
                      let http = response as? HTTPURLResponse,
                      (200..<300).contains(http.statusCode) else {
                    throw HelperError.invalidReceipt
                }
                return
            } catch {
                guard attempt < 2 else { return }
                try? await Task.sleep(for: .seconds(attempt + 1))
            }
        }
    }

    private static func removeReceiptDirectory(_ receipt: ValidatedReceipt) {
        let temporaryURL = FileManager.default.temporaryDirectory
            .resolvingSymlinksInPath().standardizedFileURL
        guard receipt.directoryURL.deletingLastPathComponent().path == temporaryURL.path,
              isValidReceiptDirectoryName(receipt.directoryURL.lastPathComponent),
              !isSymbolicLink(at: receipt.directoryURL),
              resourceIdentifier(at: receipt.directoryURL)?
                .isEqual(receipt.directoryResourceIdentifier) == true else {
            return
        }
        try? FileManager.default.removeItem(at: receipt.directoryURL)
    }

    private static func isValidReceiptDirectoryName(_ name: String) -> Bool {
        let prefix = "ZhuoDazi-Uninstall-"
        guard name.hasPrefix(prefix) else { return false }
        let identifier = String(name.dropFirst(prefix.count))
        guard let uuid = UUID(uuidString: identifier) else { return false }
        return identifier == uuid.uuidString.lowercased()
    }

    private static func isSymbolicLink(at url: URL) -> Bool {
        (try? FileManager.default.destinationOfSymbolicLink(atPath: url.path)) != nil
    }

    private static func resourceIdentifier(at url: URL) -> NSObject? {
        guard let values = try? url.resourceValues(forKeys: [.fileResourceIdentifierKey]) else { return nil }
        return values.fileResourceIdentifier as? NSObject
    }

    private static func applicationHasExpectedBundleIdentifier(at appURL: URL) -> Bool {
        let infoURL = appURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("Info.plist", isDirectory: false)
        guard let data = try? Data(contentsOf: infoURL, options: [.mappedIfSafe]),
              data.count <= 256 * 1024,
              let object = try? PropertyListSerialization.propertyList(from: data, format: nil),
              let info = object as? [String: Any] else {
            return false
        }
        return info["CFBundleIdentifier"] as? String
            == ControlledUninstallContract.applicationBundleIdentifier
    }

    private static func timestamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }
}
