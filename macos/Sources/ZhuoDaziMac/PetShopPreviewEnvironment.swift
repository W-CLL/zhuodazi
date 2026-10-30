import AppKit

struct PetShopPreviewConfiguration: Decodable {
    let baseUrl: String
    let dataDirectory: String
    let accountId: String
    let licenseId: String
    let installationId: String
    let credential: String
    let signingPublicKeySpki: String
    var origin: URL { URL(string: baseUrl)! }
    var storage: URL { URL(fileURLWithPath: dataDirectory, isDirectory: true).appendingPathComponent("mac-shop-preview") }
    static func load(_ path: String) throws -> Self {
        guard path.hasPrefix("/"), !path.hasPrefix("//"),
              let attributes = try? FileManager.default.attributesOfItem(atPath: path),
              ((attributes[.size] as? NSNumber)?.intValue ?? Int.max) <= 256 * 1024 else {
            throw PetMarketError.invalid("预览配置必须是本机绝对路径的小型 JSON 文件")
        }
        let configuration = try JSONDecoder().decode(Self.self, from: Data(contentsOf: URL(fileURLWithPath: path)))
        guard let url = URL(string: configuration.baseUrl), url.scheme == "http", url.host == "127.0.0.1",
              let port = url.port, (1...65535).contains(port), url.path == "" || url.path == "/",
              url.user == nil, url.password == nil, url.query == nil, url.fragment == nil,
              configuration.dataDirectory.hasPrefix("/"), !configuration.dataDirectory.hasPrefix("//"),
              URL(fileURLWithPath: configuration.dataDirectory).standardizedFileURL.pathComponents.count > 2,
              UUID(uuidString: configuration.licenseId) != nil, !configuration.accountId.isEmpty,
              configuration.installationId.range(of: "^[A-Za-z0-9_-]{20,80}$", options: .regularExpression) != nil,
              configuration.credential.range(of: "^[A-Za-z0-9_-]{40,80}$", options: .regularExpression) != nil else {
            throw PetMarketError.invalid("预览只允许独立本机目录与 http://127.0.0.1:端口；授权配置无效")
        }
        let production = try FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask,
            appropriateFor: nil, create: false).appendingPathComponent("ZhuoDazi").standardizedFileURL.path
        let target = configuration.storage.standardizedFileURL.path
        guard target != production, !target.hasPrefix(production + "/") else { throw PetMarketError.invalid("预览不能使用正式用户数据目录") }
        try PetMarketInstaller.prepareDirectory(configuration.storage)
        return configuration
    }
}

/// A dedicated shop preview exits AppDelegate startup before trial checks,
/// analytics, production settings, updater or companion polling are initialized.
@MainActor
final class PetShopPreviewHost {
    private struct State: Codable {
        var pets: [PetMarketInstallation] = []
        var words: [InteractionWordPackDefinition] = []
        var scripts: [TheaterScriptDefinition] = []
        var enabledPets: [String] = []
    }
    private var state = State()
    private let stateURL: URL
    private let client: PetMarketClient
    private var controller: PetShopViewController!
    private var window: NSWindow!
    init(configuration: PetShopPreviewConfiguration) throws {
        stateURL = configuration.storage.appendingPathComponent("preview-state.json")
        if let bytes = try? Data(contentsOf: stateURL) { state = try JSONDecoder().decode(State.self, from: bytes) }
        let license = try LicenseService(preview: configuration)
        client = try PetMarketClient(licenses: license, preview: configuration)
        controller = PetShopViewController(licenses: license, client: client,
            installedPets: { [weak self] in self?.state.pets ?? [] },
            onInstallPet: { [weak self] item in
                guard let self else { return }; state.pets.removeAll { $0.productId == item.productId }; state.pets.append(item)
                state.enabledPets.removeAll { $0 == item.productId }; save()
            }, onRefreshPet: { [weak self] item in
                guard let self, let index = state.pets.firstIndex(where: { $0.productId == item.productId }) else { return }
                state.pets[index] = item; save()
            }, isPetEnabled: { [weak self] id in self?.state.enabledPets.contains(id) == true },
            onTogglePet: { [weak self] id, enabled in
                guard let self else { return }
                if enabled {
                    guard state.enabledPets.count < 3, let pet = state.pets.first(where: { $0.productId == id }),
                          client.hasValidStoredUseGrant(pet) else { throw PetMarketError.invalid("最多启用3个来源，且须具有有效使用授权") }
                    state.enabledPets.append(id)
                } else { state.enabledPets.removeAll { $0 == id } }
                save()
            }, resourceState: { [weak self] id, hash in
                guard let self else { return "not-installed" }
                let words = state.words.filter { $0.sourceResourcePackId == id && $0.sourceResourcePackSha256 == hash }
                let scripts = state.scripts.filter { $0.sourceResourcePackId == id && $0.sourceResourcePackSha256 == hash }
                guard !words.isEmpty || !scripts.isEmpty else { return "not-installed" }
                return words.allSatisfy(\.enabled) && scripts.allSatisfy(\.enabled) ? "enabled" : "installed-disabled"
            }, onInstallResources: { [weak self] parsed in
                guard let self else { return }
                state.words.removeAll { $0.sourceResourcePackId == parsed.packId }; state.words += parsed.words
                state.scripts.removeAll { $0.sourceResourcePackId == parsed.packId }; state.scripts += parsed.scripts; save()
            }, onToggleResources: { [weak self] id in
                guard let self else { return }
                let indices = state.words.indices.filter { self.state.words[$0].sourceResourcePackId == id }
                let scriptIndices = state.scripts.indices.filter { self.state.scripts[$0].sourceResourcePackId == id }
                let enable = !(indices.allSatisfy { self.state.words[$0].enabled } && scriptIndices.allSatisfy { self.state.scripts[$0].enabled })
                if enable {
                    guard state.words.filter({ $0.enabled && $0.sourceResourcePackId != id }).count + indices.count <= 10,
                          state.scripts.filter({ $0.enabled && $0.sourceResourcePackId != id }).count + scriptIndices.count <= 10 else {
                        throw PetMarketError.invalid("互动词库和小剧场分别最多启用10个")
                    }
                }
                indices.forEach { self.state.words[$0].enabled = enable }; scriptIndices.forEach { self.state.scripts[$0].enabled = enable }; save()
            }, requestActivation: { presentFriendlyError(LicenseError.inactive, title: "需要本地测试授权") })
        window = NSWindow(contentViewController: controller)
        window.title = "桌宠小铺 · 本地独立预览"
        window.styleMask = [.titled, .closable, .miniaturizable, .resizable]
        window.setContentSize(NSSize(width: 1040, height: 730)); window.minSize = NSSize(width: 780, height: 580)
        window.isReleasedWhenClosed = false; window.center()
        NSApplication.shared.setActivationPolicy(.regular)
        show()
    }
    private func save() {
        do { try JSONEncoder().encode(state).write(to: stateURL, options: .atomic) }
        catch { presentFriendlyError(error, title: "本地预览状态保存失败") }
    }
    func show() { window.makeKeyAndOrderFront(nil); NSApplication.shared.activate(ignoringOtherApps: true) }
    func stop() { controller.stop() }
}
