import AppKit
import Foundation
import ServiceManagement

enum ContentImportError: LocalizedError {
    case invalidWordPack
    case invalidTheaterScript
    case unsupportedGIF
    case limitReached(String)

    var errorDescription: String? {
        switch self {
        case .invalidWordPack: return "词包格式无效，请使用 JSON 或带动作分组的 TXT 文件"
        case .invalidTheaterScript: return "剧本至少需要 3 轮、最多 5 轮完整对话"
        case .unsupportedGIF: return "请选择有效的 GIF 图片"
        case .limitReached(let message): return message
        }
    }
}

enum ContentStorage {
    static func importPet(from source: URL) throws -> PetDefinition {
        guard source.pathExtension.caseInsensitiveCompare("gif") == .orderedSame,
              NSImage(contentsOf: source) != nil else { throw ContentImportError.unsupportedGIF }
        let directory = try applicationSupportDirectory().appendingPathComponent("Pets", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let baseName = source.deletingPathExtension().lastPathComponent.cleaned(limit: 40)
        let destination = directory.appendingPathComponent("\(UUID().uuidString).gif")
        try FileManager.default.copyItem(at: source, to: destination)
        return PetDefinition(name: baseName.isEmpty ? "自定义桌宠" : baseName, path: destination.path)
    }

    static func deleteImportedPet(_ pet: PetDefinition) {
        guard let support = try? applicationSupportDirectory().standardizedFileURL.path,
              URL(fileURLWithPath: pet.path).standardizedFileURL.path.hasPrefix(support) else { return }
        try? FileManager.default.removeItem(atPath: pet.path)
    }

    private static func applicationSupportDirectory() throws -> URL {
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let directory = base.appendingPathComponent("ZhuoDazi", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }
}

enum InteractionWordPackImporter {
    static let actions = [
        "idle", "grab", "switch", "happy", "angry", "confused", "shy", "sleepy",
        "surprised", "cheer", "sad", "calm", "chase", "dodge", "bounce",
        "theater_open", "theater_reply", "theater_middle", "theater_middle_reply",
        "theater_challenge", "theater_challenge_reply", "theater_twist",
        "theater_twist_reply", "theater_finish"
    ]

    private static let aliases: [String: String] = [
        "待机": "idle", "抓起": "grab", "切换": "switch", "开心": "happy", "生气": "angry",
        "疑惑": "confused", "害羞": "shy", "困倦": "sleepy", "惊讶": "surprised", "加油": "cheer",
        "难过": "sad", "安静": "calm", "追逐": "chase", "躲避": "dodge", "反弹": "bounce",
        "小剧场开场": "theater_open", "小剧场回应": "theater_reply", "小剧场发展": "theater_middle",
        "小剧场发展回应": "theater_middle_reply", "小剧场挑战": "theater_challenge",
        "小剧场挑战回应": "theater_challenge_reply", "小剧场转折": "theater_twist",
        "小剧场转折回应": "theater_twist_reply", "小剧场收尾": "theater_finish"
    ]

    static func load(from url: URL) throws -> InteractionWordPackDefinition {
        let data = try Data(contentsOf: url, options: .mappedIfSafe)
        guard data.count <= 256 * 1024 else { throw ContentImportError.invalidWordPack }
        let name = url.deletingPathExtension().lastPathComponent.cleaned(limit: 40)
        let words: [String: [String]]
        if url.pathExtension.caseInsensitiveCompare("json") == .orderedSame {
            words = try parseJSON(data)
        } else {
            guard let text = String(data: data, encoding: .utf8) else { throw ContentImportError.invalidWordPack }
            words = parseText(text)
        }
        guard !words.isEmpty else { throw ContentImportError.invalidWordPack }
        return InteractionWordPackDefinition(name: name.isEmpty ? "互动词包" : name, words: words)
    }

    private static func parseJSON(_ data: Data) throws -> [String: [String]] {
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw ContentImportError.invalidWordPack
        }
        let container = (root["reactions"] as? [String: Any]) ?? (root["words"] as? [String: Any]) ?? root
        var result: [String: [String]] = [:]
        for (rawAction, value) in container {
            guard let action = normalizedAction(rawAction), let values = value as? [Any] else { continue }
            let lines = cleanLines(values.compactMap { $0 as? String })
            if !lines.isEmpty { result[action] = lines }
        }
        return result
    }

    private static func parseText(_ text: String) -> [String: [String]] {
        var result: [String: [String]] = [:]
        var currentAction: String?
        for rawLine in text.components(separatedBy: .newlines) {
            let line = rawLine.trimmingCharacters(in: .whitespacesAndNewlines)
            if line.isEmpty || line.hasPrefix("#") || line.hasPrefix("//") { continue }
            let header = line.trimmingCharacters(in: CharacterSet(charactersIn: "[]【】"))
            if let action = normalizedAction(header) {
                currentAction = action
                continue
            }
            if let separator = line.firstIndex(where: { $0 == ":" || $0 == "：" }) {
                let left = String(line[..<separator])
                if let action = normalizedAction(left) {
                    let value = String(line[line.index(after: separator)...]).cleaned(limit: 60)
                    if !value.isEmpty { result[action, default: []].append(value) }
                    currentAction = action
                    continue
                }
            }
            if let currentAction {
                let value = line.trimmingCharacters(in: CharacterSet(charactersIn: "-• ")).cleaned(limit: 60)
                if !value.isEmpty { result[currentAction, default: []].append(value) }
            }
        }
        return result.mapValues(cleanLines)
    }

    private static func normalizedAction(_ value: String) -> String? {
        let key = value.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if actions.contains(key) { return key }
        return aliases[key]
    }

    private static func cleanLines(_ lines: [String]) -> [String] {
        var seen = Set<String>()
        return Array(lines.map { $0.cleaned(limit: 60) }.filter { !$0.isEmpty && seen.insert($0).inserted }.prefix(50))
    }

    static let guide = """
    JSON 示例：
    {"reactions":{"happy":["今天也要开心！"],"grab":["慢一点，我要起飞了。"],"theater_open":["听说今天有大事发生。"]}}

    TXT 示例：
    [开心]
    今天也要开心！
    [抓起]
    慢一点，我要起飞了。

    支持动作：\(actions.joined(separator: ", "))
    每个动作最多 50 句，每句最多 60 个字符。
    """
}

enum TheaterScriptImporter {
    static func load(from url: URL) throws -> TheaterScriptDefinition {
        let data = try Data(contentsOf: url, options: .mappedIfSafe)
        guard data.count <= 256 * 1024,
              let root = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let rawScenes = root["scenes"] as? [[String: Any]] else {
            throw ContentImportError.invalidTheaterScript
        }
        let scenes = rawScenes.compactMap { item -> TheaterSceneDefinition? in
            let main = ((item["main"] ?? item["actorA"]) as? String ?? "").cleaned(limit: 60)
            let companion = ((item["companion"] ?? item["actorB"]) as? String ?? "").cleaned(limit: 60)
            return main.isEmpty || companion.isEmpty ? nil : TheaterSceneDefinition(main: main, companion: companion)
        }
        guard scenes.count >= 3 else { throw ContentImportError.invalidTheaterScript }
        let fallback = url.deletingPathExtension().lastPathComponent
        let name = ((root["name"] as? String) ?? fallback).cleaned(limit: 40)
        return TheaterScriptDefinition(name: name.isEmpty ? "小剧场剧本" : name, scenes: Array(scenes.prefix(5)))
    }

    static let guide = """
    JSON 示例：
    {"name":"周一摸鱼大会","scenes":[
      {"main":"我宣布，今天的任务是准时下班。","companion":"收到，我负责盯住时钟。"},
      {"main":"要是临时又来需求呢？","companion":"先深呼吸，再把优先级问清楚。"},
      {"main":"计划听起来很稳。","companion":"最后记得保存文件，我们撤！"}
    ]}

    每个剧本需要 3 至 5 轮完整对话，每句最多 60 个字符；也兼容 actorA/actorB 字段。
    """
}

enum LoginItemService {
    static func setEnabled(_ enabled: Bool) throws {
        if enabled {
            if SMAppService.mainApp.status != .enabled { try SMAppService.mainApp.register() }
        } else if SMAppService.mainApp.status == .enabled {
            try SMAppService.mainApp.unregister()
        }
    }

    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }
}
