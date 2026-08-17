import Foundation

struct FeedbackQuota: Decodable {
    let active: Int
    let maximum: Int
    let remaining: Int
}

struct FeedbackItem: Decodable {
    let id: String
    let type: String
    let title: String
    let content: String
    let status: String
    let adminNote: String
    let createdAt: String
    let updatedAt: String
}

struct FeedbackListResponse: Decodable {
    let quota: FeedbackQuota
    let items: [FeedbackItem]
}

private struct FeedbackSubmitResponse: Decodable {
    let quota: FeedbackQuota
    let item: FeedbackItem
}

private struct FeedbackSubmission: Encodable {
    let type: String
    let title: String
    let content: String
}

enum FeedbackError: LocalizedError {
    case invalidResponse
    case server(String)

    var errorDescription: String? {
        switch self {
        case .invalidResponse:
            return "反馈服务返回的数据无效"
        case .server(let message):
            return message
        }
    }
}

final class FeedbackService {
    private static let feedbackURL = DeskPetApi.feedback
    private static let maximumResponseBytes = 256 * 1024
    private let licenses: LicenseService
    private let decoder = JSONDecoder()
    private let encoder = JSONEncoder()

    init(licenses: LicenseService) {
        self.licenses = licenses
    }

    func list() async throws -> FeedbackListResponse {
        var request = URLRequest(url: Self.feedbackURL)
        request.timeoutInterval = 25
        try authorize(&request)
        return try await send(request, as: FeedbackListResponse.self)
    }

    func submit(type: String, title: String, content: String) async throws {
        var request = URLRequest(url: Self.feedbackURL)
        request.httpMethod = "POST"
        request.timeoutInterval = 25
        request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        request.httpBody = try encoder.encode(FeedbackSubmission(type: type, title: title, content: content))
        try authorize(&request)
        _ = try await send(request, as: FeedbackSubmitResponse.self)
    }

    private func authorize(_ request: inout URLRequest) throws {
        try licenses.authorize(&request)
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
    }

    private func send<T: Decodable>(_ request: URLRequest, as type: T.Type) async throws -> T {
        let (data, response) = try await URLSession.shared.data(for: request)
        guard data.count <= Self.maximumResponseBytes,
              let http = response as? HTTPURLResponse else {
            throw FeedbackError.invalidResponse
        }
        guard (200..<300).contains(http.statusCode) else {
            throw FeedbackError.server(Self.readError(data) ?? "反馈请求失败，请稍后重试")
        }
        guard let result = try? decoder.decode(type, from: data) else {
            throw FeedbackError.invalidResponse
        }
        return result
    }

    private static func readError(_ data: Data) -> String? {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let message = object["error"] as? String,
              !message.isEmpty else { return nil }
        return message
    }
}
