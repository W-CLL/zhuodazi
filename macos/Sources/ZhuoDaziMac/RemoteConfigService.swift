import Foundation

struct RemoteConfig: Equatable {
    var wechatId = "wcl_lcw627"
    var announcement = ""
    var xianyuUrl = ""
    var trialVisits = true
    var companionHall = true
    var fishMode = true
    var autoUpdates = true
    var personality = "lively"
    var interactionMode = "standard"
    var theaterIntervalSeconds = 300
}

final class RemoteConfigService {
    private let defaults: UserDefaults
    private let cacheKey = "com.zhuodazi.desktop-pet.remote-config.v1"
    private(set) var current = RemoteConfig()

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        if let data = defaults.data(forKey: cacheKey) {
            current = Self.parse(data) ?? RemoteConfig()
        }
    }

    @discardableResult
    func refresh() async -> Bool {
        var request = URLRequest(url: DeskPetApi.siteSettings)
        request.timeoutInterval = 8
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else { return false }
            guard let parsed = Self.parse(data) else { return false }
            current = parsed
            defaults.set(data, forKey: cacheKey)
            return true
        } catch {
            return false
        }
    }

    private static func parse(_ data: Data) -> RemoteConfig? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        let features = root["features"] as? [String: Any] ?? [:]
        let defaults = root["defaults"] as? [String: Any] ?? [:]
        let personality = string(defaults["personality"], fallback: "lively")
        let interactionMode = string(defaults["interactionMode"], fallback: "standard")
        let theaterInterval = int(defaults["theaterIntervalSeconds"], fallback: 300)
        let wechatId = string(root["wechatId"], fallback: "wcl_lcw627")
        return RemoteConfig(
            wechatId: isWechatId(wechatId) ? wechatId : "wcl_lcw627",
            announcement: string(root["announcement"], fallback: "").replacingOccurrences(of: "\n", with: " ").trimmingCharacters(in: .whitespacesAndNewlines),
            xianyuUrl: string(root["xianyuUrl"], fallback: ""),
            trialVisits: bool(features["trialVisits"], fallback: true),
            companionHall: bool(features["companionHall"], fallback: true),
            fishMode: bool(features["fishMode"], fallback: true),
            autoUpdates: bool(features["autoUpdates"], fallback: true),
            personality: ["lively", "shy", "clingy", "chaotic"].contains(personality) ? personality : "lively",
            interactionMode: ["quiet", "standard", "lively"].contains(interactionMode) ? interactionMode : "standard",
            theaterIntervalSeconds: [60, 180, 300, 600, 1800].contains(theaterInterval) ? theaterInterval : 300
        )
    }

    private static func string(_ value: Any?, fallback: String) -> String {
        (value as? String)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? fallback
    }

    private static func bool(_ value: Any?, fallback: Bool) -> Bool {
        if let flag = value as? Bool { return flag }
        return fallback
    }

    private static func int(_ value: Any?, fallback: Int) -> Int {
        if let number = value as? Int { return number }
        if let number = value as? Double { return Int(number) }
        return fallback
    }

    private static func isWechatId(_ value: String) -> Bool {
        value.range(of: "^[A-Za-z][-_A-Za-z0-9]{5,19}$", options: .regularExpression) != nil
    }
}
