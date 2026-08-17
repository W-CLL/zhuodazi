import Foundation

final class AnalyticsService {
    private static let eventsURL = DeskPetApi.analyticsEvents
    private let installationId: String
    private let architecture: String
    private let firstLaunchMarkerURL: URL

    init(licenses: LicenseService) {
        installationId = licenses.installationId
#if arch(arm64)
        architecture = "arm64"
#else
        architecture = "x86_64"
#endif
        let directory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("ZhuoDazi", isDirectory: true)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        firstLaunchMarkerURL = directory.appendingPathComponent("analytics-first-launch.marker")
    }

    func trackStartup() {
        let occurredAt = ISO8601DateFormatter().string(from: Date())
        let types = FileManager.default.fileExists(atPath: firstLaunchMarkerURL.path)
            ? ["app_session_start", "app_daily_active"]
            : ["app_first_launch", "app_session_start", "app_daily_active"]
        let events = types.map { type in
            [
                "eventId": "macos-\(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased())",
                "type": type,
                "installationId": installationId,
                "platform": "macos",
                "architecture": architecture,
                "version": AppVersion.current,
                "occurredAt": occurredAt
            ]
        }
        var request = URLRequest(url: Self.eventsURL)
        request.httpMethod = "POST"
        request.timeoutInterval = 12
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("ZhuoDazi/\(AppVersion.current)", forHTTPHeaderField: "User-Agent")
        request.setValue("macos", forHTTPHeaderField: "X-DeskPet-Platform")
        request.setValue(architecture, forHTTPHeaderField: "X-DeskPet-Architecture")
        guard let body = try? JSONSerialization.data(withJSONObject: ["events": events]) else { return }
        request.httpBody = body
        Task {
            for attempt in 0..<3 {
                guard let (_, response) = try? await URLSession.shared.data(for: request),
                      let http = response as? HTTPURLResponse else {
                    if attempt < 2 { try? await Task.sleep(for: .seconds(attempt + 1)) }
                    continue
                }
                if (200..<300).contains(http.statusCode) {
                    if !FileManager.default.fileExists(atPath: firstLaunchMarkerURL.path) {
                        try? Data(occurredAt.utf8).write(to: firstLaunchMarkerURL, options: .atomic)
                    }
                    return
                }
                if attempt < 2 { try? await Task.sleep(for: .seconds(attempt + 1)) }
            }
        }
    }
}
