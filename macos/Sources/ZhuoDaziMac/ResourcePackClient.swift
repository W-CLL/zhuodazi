import Foundation
import ZhuoDaziCore

struct ResourcePackProduct: Decodable, Identifiable {
    var id: String
    var category: String
    var title: String
    var description: String
    var version: String
    var size: Int
    var sha256: String
    var fileCount: Int
    var isWords: Bool { category == "interaction-words" }
    var categoryLabel: String { isWords ? "互动词库" : "小剧场" }
}
struct ParsedResourcePack {
    var packId: String
    var sha256: String
    var words: [InteractionWordPackDefinition]
    var scripts: [TheaterScriptDefinition]
}

@MainActor
final class ResourcePackClient {
    private let transport: PetMarketClient
    init(transport: PetMarketClient) { self.transport = transport }
    func catalog() async throws -> [ResourcePackProduct] {
        struct Catalog: Decodable { var packs: [ResourcePackProduct] }
        let catalog = try JSONDecoder().decode(Catalog.self,
            from: await transport.data("/api/public/resource-packs"))
        guard catalog.packs.count <= 2000 else { throw PetMarketError.invalid("资源目录过大") }
        for pack in catalog.packs { try Self.validate(pack) }
        return catalog.packs
    }
    func install(_ pack: ResourcePackProduct) async throws -> ParsedResourcePack {
        try Self.validate(pack)
        let data = try await transport.data("/resource-packs/\(pack.id)/download", limit: pack.size)
        guard data.count == pack.size, PetMarketClient.hash(data) == pack.sha256.lowercased() else {
            throw PetMarketError.invalid("资源完整性校验失败，请刷新后重试")
        }
        let work = Task.detached(priority: .utility) { try Self.parse(data, pack: pack) }
        return try await withTaskCancellationHandler(operation: {
            try await work.value
        }, onCancel: { work.cancel() })
    }
    private nonisolated static func validate(_ pack: ResourcePackProduct) throws {
        guard UUID(uuidString: pack.id) != nil, ["interaction-words", "theater-scripts"].contains(pack.category),
              (1...50 * 1024 * 1024).contains(pack.size), (1...80).contains(pack.fileCount),
              PetMarketJSON.validHash(pack.sha256), PetMarketJSON.validPart(pack.version, maximum: 40) else {
            throw PetMarketError.invalid("资源目录格式无效")
        }
    }
    private nonisolated static func parse(_ data: Data, pack: ResourcePackProduct) throws -> ParsedResourcePack {
        let entries = try ShopArchive.read(data, maxFiles: 162, maxFileBytes: 256 * 1024, maxTotalBytes: 20 * 1024 * 1024)
        var words: [InteractionWordPackDefinition] = [], scripts: [TheaterScriptDefinition] = []
        for entry in entries {
            try Task.checkCancellation()
            if !entry.path.lowercased().hasSuffix(".json") {
                guard ["readme.txt", "readme.md"].contains(URL(fileURLWithPath: entry.path).lastPathComponent.lowercased()) else {
                    throw PetMarketError.invalid("资源包只能包含 JSON 和 README")
                }
                continue
            }
            guard let text = String(data: entry.data, encoding: .utf8),
                  let json = text.trimmingCharacters(in: CharacterSet(charactersIn: "\u{feff}")).data(using: .utf8),
                  let object = try JSONSerialization.jsonObject(with: json) as? [String: Any] else {
                throw PetMarketError.invalid("资源内容不是有效 JSON")
            }
            let fallback = URL(fileURLWithPath: entry.path).deletingPathExtension().lastPathComponent
            let title = String(((object["name"] as? String) ?? fallback).trimmingCharacters(in: .whitespacesAndNewlines).prefix(40))
            if pack.isWords {
                let source = (object["reactions"] as? [String: Any]) ?? (object["words"] as? [String: Any]) ?? object
                var reactions: [String: [String]] = [:]
                for (raw, value) in source {
                    let normalized = raw.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
                    let action = aliases[normalized] ?? normalized
                    guard InteractionWordPackImporter.actions.contains(action) else { continue }
                    let values = (value as? [String]) ?? (value as? String).map { [$0] } ?? []
                    var seen = Set<String>()
                    let clean = values.map { String($0.trimmingCharacters(in: .whitespacesAndNewlines).prefix(60)) }
                        .filter { !$0.isEmpty && seen.insert($0).inserted }
                    if !clean.isEmpty { reactions[action, default: []].append(contentsOf: clean.prefix(50)) }
                }
                guard !reactions.isEmpty else { throw PetMarketError.invalid("词库没有可用台词") }
                var word = InteractionWordPackDefinition(name: title.isEmpty ? pack.title : title, words: reactions)
                word.sourceResourcePackId = pack.id; word.sourceResourcePackSha256 = pack.sha256; word.enabled = false
                words.append(word)
            } else {
                guard let scenes = object["scenes"] as? [[String: Any]], (3...5).contains(scenes.count) else {
                    throw PetMarketError.invalid("剧本需要 3–5 轮完整对白")
                }
                let parsed = try scenes.map { scene -> TheaterSceneDefinition in
                    guard let main = (scene["main"] ?? scene["actorA"]) as? String,
                          let companion = (scene["companion"] ?? scene["actorB"]) as? String,
                          !main.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                          !companion.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                        throw PetMarketError.invalid("剧本对白不完整")
                    }
                    return TheaterSceneDefinition(main: String(main.prefix(60)), companion: String(companion.prefix(60)))
                }
                var script = TheaterScriptDefinition(name: title.isEmpty ? pack.title : title, scenes: parsed)
                script.sourceResourcePackId = pack.id; script.sourceResourcePackSha256 = pack.sha256; script.enabled = false
                scripts.append(script)
            }
        }
        guard words.count + scripts.count == pack.fileCount else { throw PetMarketError.invalid("资源文件数与目录不符") }
        return ParsedResourcePack(packId: pack.id, sha256: pack.sha256, words: words, scripts: scripts)
    }
    private nonisolated static let aliases = ["待机":"idle", "抓我":"grab", "抓取":"grab", "抓起":"grab",
        "切换":"switch", "开心":"happy", "生气":"angry", "疑惑":"confused", "害羞":"shy", "困倦":"sleepy", "困":"sleepy",
        "惊讶":"surprised", "加油":"cheer", "难过":"sad", "伤心":"sad", "安静":"calm", "追逐":"chase", "躲避":"dodge", "反弹":"bounce",
        "小剧场开场":"theater_open", "小剧场回应":"theater_reply", "小剧场中场":"theater_middle", "小剧场中场回应":"theater_middle_reply",
        "小剧场动作":"theater_challenge", "小剧场动作回应":"theater_challenge_reply", "小剧场反转":"theater_twist",
        "小剧场反转回应":"theater_twist_reply", "小剧场收尾":"theater_finish"]
}
