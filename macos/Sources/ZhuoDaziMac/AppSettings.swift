import Foundation

struct AppSettings: Codable {
    var mouseInteractionEnabled = true
    var randomMovementEnabled = true
    var randomPetEnabled = true
    var alwaysOnTop = true
    var dockIconVisible = true
    var positionX: CGFloat?
    var positionY: CGFloat?
}

final class SettingsStore {
    private let defaults: UserDefaults
    private let key = "com.zhuodazi.desktop-pet.settings.v2"

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    func load() -> AppSettings {
        guard let data = defaults.data(forKey: key),
              let settings = try? JSONDecoder().decode(AppSettings.self, from: data) else {
            return AppSettings()
        }
        return settings
    }

    func save(_ settings: AppSettings) {
        guard let data = try? JSONEncoder().encode(settings) else { return }
        defaults.set(data, forKey: key)
    }
}
