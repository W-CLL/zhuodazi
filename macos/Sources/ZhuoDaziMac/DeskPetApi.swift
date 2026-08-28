import Foundation

enum DeskPetApi {
    static let host = "in.desktoppet.online"
    static let baseURL = URL(string: "https://\(host)")!
    static let signingPublicKeySPKI = "MCowBQYDK2VwAyEANjBEMMQ5TY+0ECNoRqQy9780eoVOzkKpzFDq2TwLytU="
    static let downloadPathPrefix = "/downloads/"

    static let activate = baseURL.appendingPathComponent("api/activate")
    static let trial = baseURL.appendingPathComponent("api/trial")
    static let feedback = baseURL.appendingPathComponent("api/feedback")
    static let interactionProfile = baseURL.appendingPathComponent("api/interactions/profile")
    static let interactionEvents = baseURL.appendingPathComponent("api/interactions/events")
    static let contentBatch = baseURL.appendingPathComponent("api/content/batch")
    static let contentOfflinePack = baseURL.appendingPathComponent("api/content/offline-pack")
    static let companion = baseURL.appendingPathComponent("api/companion")
    static let companionPair = companion.appendingPathComponent("pair")
    static let companionDeliveries = companion.appendingPathComponent("deliveries")
    static let companionHall = companion.appendingPathComponent("hall")
    static let companionHallDeliveries = companionHall.appendingPathComponent("deliveries")
    static let trialVisitPlay = baseURL.appendingPathComponent("api/trial/visit-stickers/play")
    static let analyticsEvents = baseURL.appendingPathComponent("api/analytics/events")
    static let updateLatest = baseURL.appendingPathComponent("api/update/latest")
    static let siteSettings = baseURL.appendingPathComponent("api/public/site-settings")
}
