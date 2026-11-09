import Foundation
import ZhuoDaziCore

/// JSON calls keep a short timeout. Large assets get a longer idle allowance,
/// while a signed package can never outlive its already verified download grant.
struct PetMarketTransferPolicy {
    let idleTimeout: TimeInterval
    let resourceTimeout: TimeInterval
    var acceptsBinary = false

    static let api = PetMarketTransferPolicy(idleTimeout: 25, resourceTimeout: 60)
    static let asset = PetMarketTransferPolicy(idleTimeout: 120, resourceTimeout: 600, acceptsBinary: true)

    static func archive(validFor remaining: TimeInterval) throws -> PetMarketTransferPolicy {
        guard remaining.isFinite, remaining > 0 else {
            throw PetMarketError.invalid("下载授权已到期，请重新下载安装")
        }
        let deadline = min(remaining, 15 * 60)
        return PetMarketTransferPolicy(idleTimeout: min(120, deadline), resourceTimeout: deadline, acceptsBinary: true)
    }
}

/// URLSession delivers bounded chunks on its own serial delegate queue. In
/// particular, receiving a 300 MB pack must not iterate 300 million bytes on
/// MainActor or schedule a cancellation check for every individual byte.
final class PetMarketTransfer: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    private let origin: URL
    private let maximum: Int
    private let policy: PetMarketTransferPolicy
    private let configuration: URLSessionConfiguration
    private let stateLock = NSLock()
    private var continuation: CheckedContinuation<Data, Error>?
    private var terminalResult: Result<Data, Error>?
    private var session: URLSession?
    private var task: URLSessionDataTask?

    // Only accessed on the serial delegate queue; cancellation never touches
    // the buffer. The lock above protects completion and task publication only.
    private var received = Data()
    private var response: HTTPURLResponse?
    private var responseLimit = 0

    init(origin: URL, maximum: Int, policy: PetMarketTransferPolicy,
         configuration: URLSessionConfiguration = .ephemeral) {
        self.origin = origin
        self.maximum = maximum
        self.policy = policy
        self.configuration = configuration
    }

    func data(for request: URLRequest) async throws -> Data {
        guard maximum > 0, maximum <= PetMarketLimits.maximumArchiveBytes,
              let url = request.url, sameOrigin(url) else {
            throw PetMarketError.invalid("已阻止不属于小铺的请求地址")
        }
        return try await withTaskCancellationHandler(operation: {
            try await withCheckedThrowingContinuation { continuation in
                start(request, continuation: continuation)
            }
        }, onCancel: { self.finish(.failure(CancellationError())) })
    }

    private func start(_ request: URLRequest, continuation: CheckedContinuation<Data, Error>) {
        stateLock.lock()
        if let result = terminalResult {
            stateLock.unlock()
            continuation.resume(with: result)
            return
        }
        self.continuation = continuation
        configuration.timeoutIntervalForRequest = policy.idleTimeout
        configuration.timeoutIntervalForResource = policy.resourceTimeout
        configuration.httpCookieStorage = nil
        configuration.urlCredentialStorage = nil
        configuration.urlCache = nil
        configuration.connectionProxyDictionary = [:]
        let queue = OperationQueue()
        queue.name = "com.zhuodazi.pet-market-transfer"
        queue.maxConcurrentOperationCount = 1
        queue.qualityOfService = .utility
        let session = URLSession(configuration: configuration, delegate: self, delegateQueue: queue)
        var request = request
        request.timeoutInterval = policy.idleTimeout
        let task = session.dataTask(with: request)
        self.session = session
        self.task = task
        stateLock.unlock()
        task.resume()
    }

    private func sameOrigin(_ url: URL) -> Bool {
        url.scheme == origin.scheme && url.host == origin.host && url.port == origin.port
            && url.user == nil && url.password == nil && url.fragment == nil
    }

    private var finished: Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        return terminalResult != nil
    }

    private func finish(_ result: Result<Data, Error>) {
        stateLock.lock()
        guard terminalResult == nil else { stateLock.unlock(); return }
        terminalResult = result
        let continuation = self.continuation
        self.continuation = nil
        let session = self.session
        self.session = nil
        self.task = nil
        stateLock.unlock()
        // Also cancels an oversized/error response immediately; no background
        // transfer may continue after the awaiting install has been canceled.
        session?.invalidateAndCancel()
        continuation?.resume(with: result)
    }

    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
        finish(.failure(PetMarketError.invalid("已阻止小铺请求重定向")))
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
                    completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        guard !finished else { completionHandler(.cancel); return }
        guard let http = response as? HTTPURLResponse, let url = http.url, sameOrigin(url) else {
            completionHandler(.cancel)
            finish(.failure(PetMarketError.invalid("小铺响应来源无效")))
            return
        }
        let limit = (200..<300).contains(http.statusCode) ? maximum : 32 * 1024
        guard response.expectedContentLength <= Int64(limit) else {
            completionHandler(.cancel)
            finish(.failure(PetMarketError.invalid("小铺内容超过大小限制")))
            return
        }
        self.response = http
        responseLimit = limit
        completionHandler(.allow)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        guard !finished else { return }
        guard response != nil, data.count <= responseLimit - received.count else {
            finish(.failure(PetMarketError.invalid("小铺内容超过大小限制")))
            return
        }
        received.append(data)
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        guard !finished else { return }
        if let error { finish(.failure(error)); return }
        guard let response else {
            finish(.failure(PetMarketError.invalid("小铺响应来源无效")))
            return
        }
        guard (200..<300).contains(response.statusCode) else {
            let object = try? JSONSerialization.jsonObject(with: received) as? [String: Any]
            finish(.failure(PetMarketError.server(object?["error"] as? String ?? "小铺请求失败（\(response.statusCode)）")))
            return
        }
        finish(.success(received))
    }
}
