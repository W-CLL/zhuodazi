import Foundation

enum DirectNetworkSession {
    static let shared = URLSession(configuration: configuration())

    static func configuration() -> URLSessionConfiguration {
        let configuration = URLSessionConfiguration.default
        configuration.connectionProxyDictionary = [:]
        configuration.waitsForConnectivity = true
        configuration.allowsConstrainedNetworkAccess = true
        configuration.allowsExpensiveNetworkAccess = true
        return configuration
    }
}

enum NetworkConnectionErrors {
    static let connectionFailedMessage = "无法建立安全连接。应用会使用电脑的直接网络，请检查网络连接后重试。"

    static func format(_ error: Error, timeoutMessage: String) -> String {
        let networkError = error as NSError
        guard networkError.domain == NSURLErrorDomain else {
            return error.localizedDescription
        }
        if networkError.code == NSURLErrorTimedOut {
            return timeoutMessage
        }
        return connectionFailedMessage
    }
}
