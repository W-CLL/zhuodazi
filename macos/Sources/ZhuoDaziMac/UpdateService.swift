import AppKit
import CryptoKit
import Foundation

struct UpdateManifest: Decodable {
    let platform: String
    let architecture: String
    let version: String
    let url: URL
    let sha256: String
    let notes: String
    let signatureAlgorithm: String
    let signature: String
}

enum UpdatePhase: Equatable {
    case idle
    case checking
    case current
    case available
    case downloading
    case downloaded
    case failed
}

struct UpdateStatus: Equatable {
    let phase: UpdatePhase
    let message: String
    let progress: Int

    static let idle = UpdateStatus(phase: .idle, message: "可以检查更新", progress: 0)
}

enum UpdateError: LocalizedError {
    case invalidManifest(String)
    case server(String)
    case unsupportedInstallLocation
    case hashMismatch

    var errorDescription: String? {
        switch self {
        case .invalidManifest(let message), .server(let message): return message
        case .unsupportedInstallLocation: return "请先将桌搭子移动到可写入的位置，例如“应用程序”文件夹，再安装更新"
        case .hashMismatch: return "更新包校验失败，已取消安装"
        }
    }
}

final class UpdateService {
    private static let manifestURL = DeskPetApi.updateLatest
    private static let publicKeySPKI = DeskPetApi.signingPublicKeySPKI
    private let licenses: LicenseService
    private(set) var status = UpdateStatus.idle {
        didSet { statusChanged?(status) }
    }
    private(set) var availableManifest: UpdateManifest?
    private(set) var downloadedArchive: URL?
    var statusChanged: ((UpdateStatus) -> Void)?
    private var downloadDelegate: UpdateDownloadDelegate?

    init(licenses: LicenseService) {
        self.licenses = licenses
    }

    func check() async throws -> UpdateManifest? {
        setStatus(.checking, "正在检查更新", 0)
        do {
            var components = URLComponents(url: Self.manifestURL, resolvingAgainstBaseURL: false)!
            components.queryItems = [
                URLQueryItem(name: "platform", value: "macos"),
                URLQueryItem(name: "architecture", value: Self.architecture)
            ]
            var request = URLRequest(url: components.url!)
            request.timeoutInterval = 25
            request.setValue("application/json", forHTTPHeaderField: "Accept")
            try licenses.authorize(&request)

            let (data, response) = try await URLSession.shared.data(for: request)
            guard data.count <= 512 * 1024, let http = response as? HTTPURLResponse else {
                throw UpdateError.invalidManifest("更新清单无效")
            }
            if http.statusCode == 404, Self.isNoRelease(data) {
                availableManifest = nil
                setStatus(.current, "当前已经是最新版本", 0)
                return nil
            }
            guard (200..<300).contains(http.statusCode) else {
                throw UpdateError.server(Self.readError(data) ?? "检查更新失败 (\(http.statusCode))")
            }
            let manifest = try JSONDecoder().decode(UpdateManifest.self, from: data)
            try validate(manifest)
            guard AppVersion.compare(manifest.version, AppVersion.current) == .orderedDescending else {
                availableManifest = nil
                setStatus(.current, "当前已经是最新版本", 0)
                return nil
            }
            availableManifest = manifest
            setStatus(.available, "发现新版本 v\(manifest.version)", 0)
            return manifest
        } catch {
            availableManifest = nil
            setStatus(.failed, friendlyMessage(for: error), 0)
            throw error
        }
    }

    func download(_ manifest: UpdateManifest) async throws {
        setStatus(.downloading, "正在下载 v\(manifest.version)", 0)
        do {
            var request = URLRequest(url: manifest.url)
            request.timeoutInterval = 10 * 60
            try licenses.authorize(&request)
            let destination = try archiveURL(version: manifest.version)
            let delegate = UpdateDownloadDelegate(destination: destination) { [weak self] progress in
                DispatchQueue.main.async {
                    self?.setStatus(.downloading, "正在下载 v\(manifest.version)", progress)
                }
            }
            downloadDelegate = delegate
            defer { downloadDelegate = nil }
            let (archive, response) = try await delegate.download(request)
            guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
                throw UpdateError.server("下载更新失败")
            }
            guard try Self.sha256(of: archive) == manifest.sha256.lowercased() else {
                try? FileManager.default.removeItem(at: archive)
                throw UpdateError.hashMismatch
            }
            downloadedArchive = archive
            setStatus(.downloaded, "更新包已下载并通过校验", 100)
        } catch {
            downloadedArchive = nil
            setStatus(.failed, friendlyMessage(for: error), 0)
            throw error
        }
    }

    func installDownloaded() throws -> Never {
        guard Bundle.main.bundleURL.pathExtension == "app",
              FileManager.default.isWritableFile(atPath: Bundle.main.bundleURL.deletingLastPathComponent().path),
              let downloadedArchive else {
            throw UpdateError.unsupportedInstallLocation
        }
        setStatus(.downloaded, "正在重启安装", 100)
        try MacUpdateInstaller.install(archive: downloadedArchive)
        NSApplication.shared.terminate(nil)
        fatalError("Application termination returned unexpectedly")
    }

    func downloadAndInstall(_ manifest: UpdateManifest) async throws -> Never {
        try await download(manifest)
        try installDownloaded()
    }

    private func validate(_ manifest: UpdateManifest) throws {
        guard manifest.platform == "macos", manifest.architecture == Self.architecture else {
            throw UpdateError.invalidManifest("更新包的系统或架构不匹配")
        }
        guard manifest.version.range(of: "^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?$", options: .regularExpression) != nil,
              manifest.sha256.range(of: "^[0-9a-fA-F]{64}$", options: .regularExpression) != nil,
               manifest.url.scheme == "https",
               manifest.url.host?.lowercased() == DeskPetApi.host,
               manifest.url.path.hasPrefix(DeskPetApi.downloadPathPrefix),
              manifest.signatureAlgorithm.lowercased() == "ed25519",
              let signature = Data(base64Encoded: manifest.signature) else {
            throw UpdateError.invalidManifest("更新清单格式无效")
        }
        guard signature.count == 64 else { throw UpdateError.invalidManifest("更新清单签名无效") }
        let payload = try Self.signedPayload(manifest)
        guard let spki = Data(base64Encoded: Self.publicKeySPKI), spki.count >= 32 else {
            throw UpdateError.invalidManifest("内置更新公钥无效")
        }
        let publicKey = try Curve25519.Signing.PublicKey(rawRepresentation: Data(spki.suffix(32)))
        guard publicKey.isValidSignature(signature, for: payload) else {
            throw UpdateError.invalidManifest("更新清单签名校验失败")
        }
    }

    private func archiveURL(version: String) throws -> URL {
        let directory = try updatesDirectory()
        return directory.appendingPathComponent("ZhuoDazi-macOS-\(version)-\(Self.architecture).zip")
    }

    private func updatesDirectory() throws -> URL {
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let directory = base.appendingPathComponent("ZhuoDazi/Updates", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }

    private func setStatus(_ phase: UpdatePhase, _ message: String, _ progress: Int) {
        status = UpdateStatus(phase: phase, message: message, progress: progress)
    }

    private func friendlyMessage(for error: Error) -> String {
        if let updateError = error as? UpdateError { return updateError.localizedDescription }
        if let urlError = error as? URLError {
            switch urlError.code {
            case .timedOut: return "连接服务超时，请稍后重试。"
            case .notConnectedToInternet, .networkConnectionLost:
                return "暂时无法连接服务，请检查网络后重试。"
            default: return "更新服务暂时不可用，请稍后重试。"
            }
        }
        return "更新失败，请稍后重试。"
    }

    private static var architecture: String {
        #if arch(arm64)
        return "arm64"
        #else
        return "x86_64"
        #endif
    }

    private static func signedPayload(_ manifest: UpdateManifest) throws -> Data {
        let values = [manifest.version, manifest.url.absoluteString, manifest.sha256.lowercased(), manifest.notes]
        let encoded = values.map(jsonString)
        return Data("{\"version\":\(encoded[0]),\"url\":\(encoded[1]),\"sha256\":\(encoded[2]),\"notes\":\(encoded[3])}".utf8)
    }

    // Keep this byte-for-byte compatible with Node's JSON.stringify payload.
    private static func jsonString(_ value: String) -> String {
        var output = "\""
        for scalar in value.unicodeScalars {
            switch scalar.value {
            case 0x08: output += "\\b"
            case 0x09: output += "\\t"
            case 0x0a: output += "\\n"
            case 0x0c: output += "\\f"
            case 0x0d: output += "\\r"
            case 0x22: output += "\\\""
            case 0x5c: output += "\\\\"
            case 0x00...0x1f: output += String(format: "\\u%04x", scalar.value)
            default: output.unicodeScalars.append(scalar)
            }
        }
        return output + "\""
    }

    private static func sha256(of fileURL: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: fileURL)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let data = try handle.read(upToCount: 1024 * 1024), !data.isEmpty {
            hasher.update(data: data)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    private static func isNoRelease(_ data: Data) -> Bool {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return false }
        return object["code"] as? String == "NO_RELEASE"
    }

    private static func readError(_ data: Data) -> String? {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let error = object["error"] as? String,
              !error.isEmpty else { return nil }
        return error
    }
}

private final class UpdateDownloadDelegate: NSObject, URLSessionDownloadDelegate {
    private let destination: URL
    private let progressChanged: (Int) -> Void
    private var continuation: CheckedContinuation<(URL, URLResponse), Error>?
    private var session: URLSession?

    init(destination: URL, progressChanged: @escaping (Int) -> Void) {
        self.destination = destination
        self.progressChanged = progressChanged
    }

    func download(_ request: URLRequest) async throws -> (URL, URLResponse) {
        try await withCheckedThrowingContinuation { continuation in
            self.continuation = continuation
            let session = URLSession(configuration: .default, delegate: self, delegateQueue: nil)
            self.session = session
            session.downloadTask(with: request).resume()
        }
    }

    func urlSession(
        _ session: URLSession,
        downloadTask: URLSessionDownloadTask,
        didWriteData bytesWritten: Int64,
        totalBytesWritten: Int64,
        totalBytesExpectedToWrite: Int64
    ) {
        guard totalBytesExpectedToWrite > 0 else { return }
        progressChanged(min(99, Int(totalBytesWritten * 100 / totalBytesExpectedToWrite)))
    }

    func urlSession(
        _ session: URLSession,
        downloadTask: URLSessionDownloadTask,
        didFinishDownloadingTo location: URL
    ) {
        guard let response = downloadTask.response else {
            finish(.failure(UpdateError.server("下载更新失败")))
            return
        }
        do {
            try? FileManager.default.removeItem(at: destination)
            try FileManager.default.moveItem(at: location, to: destination)
            finish(.success((destination, response)))
        } catch {
            finish(.failure(error))
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if let error { finish(.failure(error)) }
    }

    private func finish(_ result: Result<(URL, URLResponse), Error>) {
        guard let continuation else { return }
        self.continuation = nil
        session?.finishTasksAndInvalidate()
        session = nil
        continuation.resume(with: result)
    }
}

private enum MacUpdateInstaller {
    static func install(archive: URL) throws {
        let appURL = Bundle.main.bundleURL
        let updatesDirectory = archive.deletingLastPathComponent()
        let stagingDirectory = updatesDirectory.appendingPathComponent("install-\(UUID().uuidString)", isDirectory: true)
        let scriptURL = updatesDirectory.appendingPathComponent("install-\(UUID().uuidString).sh")
        let logURL = updatesDirectory.appendingPathComponent("install.log")
        let script = """
        #!/bin/sh
        set -eu
        app="$1"
        archive="$2"
        pid="$3"
        staging="$4"
        log="$5"
        exec >>"$log" 2>&1
        while kill -0 "$pid" 2>/dev/null; do sleep 1; done
        /bin/mkdir -p "$staging"
        /usr/bin/ditto -x -k "$archive" "$staging"
        set -- "$staging"/*.app
        if [ "$1" = "$staging/*.app" ] || [ ! -d "$1" ]; then exit 1; fi
        replacement="$1"
        backup="${app}.previous"
        /bin/rm -rf "$backup"
        /bin/mv "$app" "$backup"
        if ! /bin/mv "$replacement" "$app"; then
            /bin/mv "$backup" "$app"
            exit 1
        fi
        /usr/bin/open "$app"
        /bin/rm -rf "$staging" "$archive" "$0"
        """
        try script.write(to: scriptURL, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: scriptURL.path)

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/nohup")
        process.arguments = [
            "/bin/sh",
            scriptURL.path,
            appURL.path,
            archive.path,
            String(ProcessInfo.processInfo.processIdentifier),
            stagingDirectory.path,
            logURL.path
        ]
        try process.run()
    }
}
