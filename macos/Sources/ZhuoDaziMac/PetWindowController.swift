import AppKit
import ZhuoDaziCore

final class PetWindowController {
    var mouseInteractionEnabled: Bool {
        get { settings.mouseInteractionEnabled }
        set { update { $0.mouseInteractionEnabled = newValue } }
    }
    var randomMovementEnabled: Bool {
        get { settings.randomMovementEnabled }
        set { update { $0.randomMovementEnabled = newValue } }
    }
    var randomPetEnabled: Bool {
        get { settings.randomPetEnabled }
        set { update { $0.randomPetEnabled = newValue } }
    }
    var alwaysOnTop: Bool {
        get { settings.alwaysOnTop }
        set { update { $0.alwaysOnTop = newValue } }
    }
    var clickThrough: Bool {
        get { settings.clickThrough }
        set { update { $0.clickThrough = newValue } }
    }
    var isVisible: Bool { window.isVisible }
    var canRandomizePet: Bool { petURLs.count > 1 }
    var currentSettings: AppSettings { settings }

    private let window: NSPanel
    private let petView: PetCanvasView
    private let petBag = RandomBag<URL>()
    private var petURLs: [URL] = []
    private var currentPetURL: URL?
    private var movementTimer: Timer?
    private var randomPetTimer: Timer?
    private var theaterTimer: Timer?
    private var velocity = CGVector.zero
    private var dragging = false
    private var dragOffset = NSPoint.zero
    private var previousDragPoint = NSPoint.zero
    private var previousDragTime = Date()
    private var nextWanderDecision = Date()
    private var lastMouseReaction = Date.distantPast
    private var settings: AppSettings
    private let settingsChanged: (AppSettings) -> Void
    private var companionWindow: NSPanel?
    private var theaterTask: Task<Void, Never>?
    private var localKeyMonitor: Any?
    private var globalKeyMonitor: Any?

    init(settings: AppSettings, settingsChanged: @escaping (AppSettings) -> Void) {
        var normalized = settings
        normalized.normalize()
        self.settings = normalized
        self.settingsChanged = settingsChanged

        let initialSize = Self.windowSize(for: normalized.size)
        let visibleFrame = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1200, height: 800)
        let defaultOrigin = NSPoint(x: visibleFrame.maxX - initialSize.width - 28, y: visibleFrame.minY + 24)
        let origin = NSPoint(x: normalized.positionX ?? defaultOrigin.x, y: normalized.positionY ?? defaultOrigin.y)
        window = NSPanel(
            contentRect: NSRect(origin: origin, size: initialSize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        petView = PetCanvasView(frame: NSRect(origin: .zero, size: initialSize))

        window.backgroundColor = .clear
        window.isOpaque = false
        window.hasShadow = false
        window.hidesOnDeactivate = false
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.contentView = petView
        window.acceptsMouseMovedEvents = true

        petView.dragBegan = { [weak self] point in self?.beginDrag(at: point) }
        petView.dragMoved = { [weak self] point in self?.continueDrag(to: point) }
        petView.dragEnded = { [weak self] point in self?.endDrag(at: point) }
        petView.clicked = { [weak self] in self?.showReaction(self?.clickAction ?? "happy") }

        applyWindowAppearance()
        reloadPetSources(forceSelection: true)
        let timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(timer, forMode: .common)
        movementTimer = timer
        installClickThroughShortcut()
        restartTimers()
    }

    deinit {
        movementTimer?.invalidate()
        randomPetTimer?.invalidate()
        theaterTimer?.invalidate()
        theaterTask?.cancel()
        if let localKeyMonitor { NSEvent.removeMonitor(localKeyMonitor) }
        if let globalKeyMonitor { NSEvent.removeMonitor(globalKeyMonitor) }
    }

    func show() { window.orderFrontRegardless() }
    func hide() { window.orderOut(nil) }
    func showBubble(_ text: String) { petView.showBubble(text) }

    func apply(_ newSettings: AppSettings) {
        var normalized = newSettings
        normalized.normalize()
        let sourcesChanged = settings.activePetId != normalized.activePetId
            || settings.activeLibraryId != normalized.activeLibraryId
            || settings.pets != normalized.pets
            || settings.libraries != normalized.libraries
        settings = normalized
        applyWindowAppearance()
        if sourcesChanged { reloadPetSources(forceSelection: true) }
        restartTimers()
        saveSettings()
    }

    func update(_ mutation: (inout AppSettings) -> Void) {
        var next = settings
        mutation(&next)
        apply(next)
    }

    @discardableResult
    func randomizePet() -> Bool {
        guard let selected = petBag.next(from: petURLs, excluding: currentPetURL) else { return false }
        currentPetURL = selected
        petView.showPet(at: selected)
        showReaction("switch")
        return true
    }

    @discardableResult
    func startTheater() -> Bool {
        guard theaterTask == nil else { return false }
        let scripts = settings.theaterScripts.isEmpty ? Self.builtInScripts : settings.theaterScripts
        guard let script = scripts.randomElement(), !script.scenes.isEmpty else { return false }
        let companion = makeCompanionWindow()
        companionWindow = companion.window
        let companionView = companion.view
        companion.window.orderFrontRegardless()
        positionCompanion(companion.window)

        theaterTask = Task { @MainActor [weak self, weak companionView] in
            guard let self, let companionView else { return }
            for (index, scene) in script.scenes.enumerated() {
                guard !Task.isCancelled else { break }
                petView.showBubble(scene.main, duration: 4.4)
                try? await Task.sleep(for: .seconds(2.1))
                guard !Task.isCancelled else { break }
                companionView.showBubble(scene.companion, duration: 4.4)
                if index < script.scenes.count - 1 { try? await Task.sleep(for: .seconds(2.5)) }
            }
            try? await Task.sleep(for: .seconds(3))
            finishTheater()
        }
        return true
    }

    func fireDueReminders(at now: Date = Date()) -> [ReminderDefinition] {
        var fired: [ReminderDefinition] = []
        var changed = false
        for index in settings.reminders.indices where settings.reminders[index].enabled && settings.reminders[index].at <= now {
            fired.append(settings.reminders[index])
            changed = true
            if settings.reminders[index].repeatDaily {
                var next = settings.reminders[index].at
                repeat { next = Calendar.current.date(byAdding: .day, value: 1, to: next) ?? next.addingTimeInterval(86_400) } while next <= now
                settings.reminders[index].at = next
            } else {
                settings.reminders[index].enabled = false
            }
        }
        if changed { saveSettings() }
        return fired
    }

    func showReminder(_ reminder: ReminderDefinition) {
        showBubble(reminder.message)
        NSSound(named: "Glass")?.play()
    }

    private var clickAction: String {
        switch settings.personality {
        case "shy": return "shy"
        case "clingy": return "happy"
        case "chaotic": return ["surprised", "confused", "cheer"].randomElement()!
        default: return "happy"
        }
    }

    private func showReaction(_ action: String) {
        let imported = settings.interactionWordPacks.first(where: { $0.id == settings.activeInteractionWordPackId })?.words[action]
        let text = imported?.randomElement() ?? Self.defaultWords[action]?.randomElement() ?? "我在这里。"
        petView.showBubble(text)
    }

    private func reloadPetSources(forceSelection: Bool) {
        if let pet = settings.pets.first(where: { $0.id == settings.activePetId }) {
            petURLs = [URL(fileURLWithPath: pet.path)]
        } else if let library = settings.libraries.first(where: { $0.id == settings.activeLibraryId }) {
            petURLs = Self.scanPetURLs(in: URL(fileURLWithPath: library.path, isDirectory: true))
        } else {
            petURLs = Self.builtInPetURLs()
        }
        if petURLs.isEmpty { petURLs = Self.builtInPetURLs() }
        if forceSelection || currentPetURL.map({ !petURLs.contains($0) }) == true {
            currentPetURL = petBag.next(from: petURLs)
            if let currentPetURL { petView.showPet(at: currentPetURL) }
        }
    }

    private func restartTimers() {
        randomPetTimer?.invalidate()
        theaterTimer?.invalidate()
        randomPetTimer = nil
        theaterTimer = nil
        if settings.randomPetEnabled, canRandomizePet {
            let timer = Timer(timeInterval: TimeInterval(settings.randomPetIntervalSeconds), repeats: true) { [weak self] _ in
                _ = self?.randomizePet()
            }
            RunLoop.main.add(timer, forMode: .common)
            randomPetTimer = timer
        }
        if settings.theaterEnabled {
            let timer = Timer(timeInterval: TimeInterval(settings.theaterIntervalSeconds), repeats: true) { [weak self] _ in
                _ = self?.startTheater()
            }
            RunLoop.main.add(timer, forMode: .common)
            theaterTimer = timer
        }
    }

    private func beginDrag(at point: NSPoint) {
        dragging = true
        velocity = .zero
        dragOffset = NSPoint(x: point.x - window.frame.origin.x, y: point.y - window.frame.origin.y)
        previousDragPoint = point
        previousDragTime = Date()
        showReaction("grab")
    }

    private func continueDrag(to point: NSPoint) {
        guard dragging else { return }
        let now = Date()
        let elapsed = CGFloat(max(now.timeIntervalSince(previousDragTime), 1.0 / 240.0))
        velocity = CGVector(dx: (point.x - previousDragPoint.x) / elapsed / 60, dy: (point.y - previousDragPoint.y) / elapsed / 60)
        previousDragPoint = point
        previousDragTime = now
        window.setFrameOrigin(NSPoint(x: point.x - dragOffset.x, y: point.y - dragOffset.y))
    }

    private func endDrag(at point: NSPoint) {
        dragging = false
        velocity.dx = max(-34, min(34, velocity.dx))
        velocity.dy = max(-34, min(34, velocity.dy))
        settings.positionX = window.frame.origin.x
        settings.positionY = window.frame.origin.y
        saveSettings()
    }

    private func tick() {
        guard window.isVisible, !dragging, theaterTask == nil else { return }
        var origin = window.frame.origin
        let frameSize = window.frame.size
        let center = NSPoint(x: origin.x + frameSize.width / 2, y: origin.y + frameSize.height / 2)

        if settings.mouseInteractionEnabled {
            let mouse = NSEvent.mouseLocation
            let deltaX = mouse.x - center.x
            let deltaY = mouse.y - center.y
            let distance = hypot(deltaX, deltaY)
            if distance > 75, distance < 560 {
                let dodge = settings.personality == "shy" || (settings.personality == "chaotic" && Int.random(in: 0...1) == 0)
                let force: CGFloat = settings.personality == "clingy" ? 0.11 : 0.075
                velocity.dx += deltaX / distance * force * (dodge ? -1 : 1)
                velocity.dy += deltaY / distance * force * (dodge ? -1 : 1)
                if Date().timeIntervalSince(lastMouseReaction) > 14 {
                    showReaction(dodge ? "dodge" : "chase")
                    lastMouseReaction = Date()
                }
            }
        }

        if settings.randomMovementEnabled, Date() >= nextWanderDecision {
            let scale: CGFloat = settings.personality == "chaotic" ? 2.4 : (settings.personality == "shy" ? 1.1 : 1.7)
            velocity.dx += CGFloat.random(in: -scale...scale)
            velocity.dy += CGFloat.random(in: -(scale * 0.65)...(scale * 0.65))
            nextWanderDecision = Date().addingTimeInterval(Double.random(in: 2.8...6.5))
        }

        velocity.dx = max(-9, min(9, velocity.dx * 0.988))
        velocity.dy = max(-9, min(9, velocity.dy * 0.988))
        origin.x += velocity.dx
        origin.y += velocity.dy

        let screen = NSScreen.screens.first(where: { $0.frame.contains(center) }) ?? NSScreen.main
        let bounds = screen?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1200, height: 800)
        if origin.x < bounds.minX {
            origin.x = bounds.minX
            velocity.dx = abs(velocity.dx) * 0.78
            showReaction("bounce")
        } else if origin.x + frameSize.width > bounds.maxX {
            origin.x = bounds.maxX - frameSize.width
            velocity.dx = -abs(velocity.dx) * 0.78
            showReaction("bounce")
        }
        if origin.y < bounds.minY {
            origin.y = bounds.minY
            velocity.dy = abs(velocity.dy) * 0.78
        } else if origin.y + frameSize.height > bounds.maxY {
            origin.y = bounds.maxY - frameSize.height
            velocity.dy = -abs(velocity.dy) * 0.78
        }
        window.setFrameOrigin(origin)
    }

    private func applyWindowAppearance() {
        let newSize = Self.windowSize(for: settings.size)
        if window.frame.size != newSize {
            window.setFrame(NSRect(origin: window.frame.origin, size: newSize), display: true)
        }
        window.isFloatingPanel = settings.alwaysOnTop
        window.level = settings.alwaysOnTop ? .floating : .normal
        window.alphaValue = CGFloat(settings.opacity) / 100
        window.ignoresMouseEvents = settings.clickThrough
        petView.setMirrored(settings.mirrored)
    }

    private func makeCompanionWindow() -> (window: NSPanel, view: PetCanvasView) {
        let size = window.frame.size
        let panel = NSPanel(contentRect: NSRect(origin: window.frame.origin, size: size), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        let view = PetCanvasView(frame: NSRect(origin: .zero, size: size))
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.level = window.level
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = view
        view.setMirrored(!settings.mirrored)
        let companionURL = petBag.next(from: petURLs, excluding: currentPetURL) ?? currentPetURL
        if let companionURL { view.showPet(at: companionURL) }
        return (panel, view)
    }

    private func positionCompanion(_ panel: NSPanel) {
        let bounds = window.screen?.visibleFrame ?? NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1200, height: 800)
        var origin = NSPoint(x: window.frame.maxX + 12, y: window.frame.minY)
        if origin.x + panel.frame.width > bounds.maxX { origin.x = window.frame.minX - panel.frame.width - 12 }
        origin.x = max(bounds.minX, min(origin.x, bounds.maxX - panel.frame.width))
        origin.y = max(bounds.minY, min(origin.y, bounds.maxY - panel.frame.height))
        panel.setFrameOrigin(origin)
    }

    private func finishTheater() {
        companionWindow?.orderOut(nil)
        companionWindow = nil
        theaterTask = nil
        showReaction("theater_finish")
    }

    private func installClickThroughShortcut() {
        let handler: (NSEvent) -> Void = { [weak self] event in
            guard event.charactersIgnoringModifiers?.lowercased() == "p",
                  event.modifierFlags.contains([.control, .shift]) else { return }
            self?.clickThrough.toggle()
        }
        localKeyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            handler(event)
            return event
        }
        globalKeyMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown, handler: handler)
    }

    private func saveSettings() { settingsChanged(settings) }

    private static func windowSize(for petSize: Int) -> NSSize {
        NSSize(width: petSize, height: petSize + 30)
    }

    private static func scanPetURLs(in directory: URL) -> [URL] {
        guard let enumerator = FileManager.default.enumerator(
            at: directory,
            includingPropertiesForKeys: [.isRegularFileKey],
            options: [.skipsHiddenFiles, .skipsPackageDescendants]
        ) else { return [] }
        return enumerator.compactMap { $0 as? URL }
            .filter { $0.pathExtension.caseInsensitiveCompare("gif") == .orderedSame }
            .sorted { $0.path.localizedCaseInsensitiveCompare($1.path) == .orderedAscending }
    }

    private static func builtInPetURLs() -> [URL] {
        let sourceRoot = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let bundledPets = Bundle.main.resourceURL?.appendingPathComponent("Pets", isDirectory: true)
        let bundledLibrary = bundledPets?.appendingPathComponent("yuexinmiao", isDirectory: true)
        let sharedLibrary = sourceRoot.deletingLastPathComponent().appendingPathComponent("windows/assets/pet-libraries/yuexinmiao", isDirectory: true)
        let fallback = sourceRoot.appendingPathComponent("Resources/Pets", isDirectory: true)
        for candidate in [bundledLibrary, sharedLibrary, bundledPets, fallback].compactMap({ $0 }) {
            let urls = scanPetURLs(in: candidate)
            if !urls.isEmpty { return urls }
        }
        return []
    }

    private static let defaultWords: [String: [String]] = [
        "happy": ["今天也要开心呀。", "你一来，我就有精神了。", "再点一下，我会更开心。"],
        "shy": ["突然靠这么近，我会害羞的。", "我、我一直都在这里。"],
        "surprised": ["欸？刚刚发生了什么！", "这个展开完全没想到。"],
        "confused": ["让我想一会儿。", "这件事好像哪里不太对。"],
        "cheer": ["今天的进度也一起拿下！", "先完成一小步，也很了不起。"],
        "grab": ["慢一点，我要起飞了。", "抓稳啦，目的地是哪里？"],
        "switch": ["换班完成，新选手登场。", "这次轮到我陪你。"],
        "chase": ["等等我，我马上跟上。", "你去哪里，我也去看看。"],
        "dodge": ["差一点就被你碰到啦。", "嘿，我闪！"],
        "bounce": ["到边界了，掉头！", "墙壁说这边不能走。"],
        "theater_finish": ["本场演出结束，谢谢观看。", "谢幕啦，下次见。"]
    ]

    private static let builtInScripts: [TheaterScriptDefinition] = [
        TheaterScriptDefinition(name: "准时下班行动", scenes: [
            TheaterSceneDefinition(main: "我宣布，今天最重要的任务是准时下班。", companion: "收到，我已经把时钟放在最显眼的位置。"),
            TheaterSceneDefinition(main: "可是待办列表看起来还很长。", companion: "先分清必须完成和可以明天继续的事情。"),
            TheaterSceneDefinition(main: "要是突然又来一个紧急需求呢？", companion: "先问截止时间和优先级，别让所有事情都变成最高级。"),
            TheaterSceneDefinition(main: "有道理，我现在专心完成手上这一项。", companion: "我负责提醒你保存文件，也提醒你起来喝水。"),
            TheaterSceneDefinition(main: "计划通过，收尾之后一起撤退！", companion: "行动代号：关电脑之前再检查一次提交。")
        ]),
        TheaterScriptDefinition(name: "零食失踪案", scenes: [
            TheaterSceneDefinition(main: "报告，我放在桌边的小饼干不见了。", companion: "先别慌，请描述它最后一次出现的位置。"),
            TheaterSceneDefinition(main: "就在键盘旁边，包装还是完整的。", companion: "现场只有你、我，还有一杯看起来很可疑的咖啡。"),
            TheaterSceneDefinition(main: "咖啡没有手，应该拿不走饼干吧？", companion: "也可能有人边想问题，边无意识地把它吃掉了。"),
            TheaterSceneDefinition(main: "等等，我口袋里为什么有一张包装纸。", companion: "证据已经出现，案件正在变得简单。"),
            TheaterSceneDefinition(main: "我承认，是过去的我给现在的我留了个谜题。", companion: "结案。下一包零食请登记后再吃。")
        ]),
        TheaterScriptDefinition(name: "灵感紧急会议", scenes: [
            TheaterSceneDefinition(main: "我盯着空白页面十分钟了，灵感还是没来。", companion: "那就先写一个绝对不会采用的版本。"),
            TheaterSceneDefinition(main: "故意写差，真的会有用吗？", companion: "空白最难修改，有了第一句就能知道哪里不满意。"),
            TheaterSceneDefinition(main: "好，我先写：这是一个非常普通的开头。", companion: "很好，现在问问自己，它怎样才会不普通。"),
            TheaterSceneDefinition(main: "也许让主角一出门就遇见会说话的桌宠。", companion: "这个方向不错，而且演员已经在现场了。"),
            TheaterSceneDefinition(main: "原来灵感不是等来的，是聊着聊着长出来的。", companion: "会议结束，趁它还热赶快写下来。")
        ])
    ]
}
