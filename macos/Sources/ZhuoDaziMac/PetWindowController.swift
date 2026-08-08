import AppKit
import QuartzCore
import ZhuoDaziCore

@MainActor
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
    var interactionStatus: String { interactions.statusSummary }

    private let window: NSPanel
    private let petView: PetCanvasView
    private let interactions: InteractionService
    private let petBag = RandomBag<URL>()
    private var petURLs: [URL] = []
    private var currentPetURL: URL?
    private var movementTimer: Timer?
    private var randomPetTimer: Timer?
    private var theaterTimer: Timer?
    private var interactionTimer: Timer?
    private var interactionSyncTimer: Timer?
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
    private let interactionPanel: PetInteractionPanelController
    private var interactionActive = false
    private var interactionSyncTask: Task<Void, Never>?
    private var interactionSyncRequested = false
    private var localKeyMonitor: Any?
    private var globalKeyMonitor: Any?

    init(
        settings: AppSettings,
        interactions: InteractionService,
        settingsChanged: @escaping (AppSettings) -> Void
    ) {
        var normalized = settings
        normalized.normalize()
        self.settings = normalized
        self.interactions = interactions
        self.settingsChanged = settingsChanged
        self.interactionPanel = PetInteractionPanelController()

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
        let timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in self?.tick() }
        }
        RunLoop.main.add(timer, forMode: .common)
        movementTimer = timer
        installClickThroughShortcut()
        restartTimers()
    }

    deinit {
        movementTimer?.invalidate()
        randomPetTimer?.invalidate()
        theaterTimer?.invalidate()
        interactionTimer?.invalidate()
        interactionSyncTimer?.invalidate()
        theaterTask?.cancel()
        interactionSyncTask?.cancel()
        if let localKeyMonitor { NSEvent.removeMonitor(localKeyMonitor) }
        if let globalKeyMonitor { NSEvent.removeMonitor(globalKeyMonitor) }
    }

    func show() { window.orderFrontRegardless() }
    func hide() {
        interactionPanel.dismiss()
        window.orderOut(nil)
    }
    func showBubble(_ text: String) { petView.showBubble(text) }

    func startInteractionServices() {
        restartInteractionTimer()
        scheduleInteractionSync(after: 1)
    }

    func apply(_ newSettings: AppSettings) {
        var normalized = newSettings
        normalized.normalize()
        let sourcesChanged = settings.activePetId != normalized.activePetId
            || settings.activeLibraryId != normalized.activeLibraryId
            || settings.pets != normalized.pets
            || settings.libraries != normalized.libraries
        let interactionChanged = settings.randomInteractionsEnabled != normalized.randomInteractionsEnabled
            || settings.interactionMode != normalized.interactionMode
        settings = normalized
        applyWindowAppearance()
        if sourcesChanged { reloadPetSources(forceSelection: true) }
        restartTimers()
        if interactionChanged {
            restartInteractionTimer()
            interactions.markProfileDirty(
                mode: settings.interactionMode,
                promptsEnabled: settings.randomInteractionsEnabled
            )
            scheduleInteractionSync(after: 2)
        }
        saveSettings()
    }

    func update(_ mutation: (inout AppSettings) -> Void) {
        var next = settings
        mutation(&next)
        apply(next)
    }

    func startRandomInteraction() {
        Task { @MainActor [weak self] in await self?.presentRandomInteraction(manual: true) }
    }

    func syncInteractionContent() async throws -> Int {
        let profile = try await interactions.syncProfile(
            localMode: settings.interactionMode,
            localPromptsEnabled: settings.randomInteractionsEnabled
        )
        applyInteractionProfile(profile)
        try await interactions.flushEvents()
        return try await interactions.refill()
    }

    func downloadInteractionPack() async throws -> Int {
        try await interactions.downloadOfflinePack()
    }

    private func presentRandomInteraction(manual: Bool) async {
        interactionTimer?.invalidate()
        interactionTimer = nil
        guard !interactionActive, theaterTask == nil, window.isVisible else {
            restartInteractionTimer()
            return
        }
        guard manual || (settings.randomInteractionsEnabled && !settings.clickThrough) else {
            restartInteractionTimer()
            return
        }
        if manual, settings.clickThrough { update { $0.clickThrough = false } }

        interactionActive = true
        do {
            let moodDue = interactions.isMoodPromptDue()
            if moodDue, interactions.cachedContentCount == 0 || Int.random(in: 0..<4) == 0 {
                showMoodInteraction()
                return
            }
            if interactions.cachedContentCount == 0 { _ = try await interactions.refill() }
            guard window.isVisible, theaterTask == nil else {
                finishInteraction()
                return
            }
            guard let item = interactions.takeNextContent() else {
                if moodDue {
                    showMoodInteraction()
                } else {
                    if manual { showBubble("趣味内容正在补货，稍后再来找我吧。") }
                    finishInteraction()
                }
                return
            }
            showContentInteraction(item)
        } catch {
            if manual { showBubble(error.localizedDescription) }
            finishInteraction()
        }
    }

    private func showMoodInteraction() {
        interactions.markMoodPrompted()
        showInteraction(
            title: "随手问候",
            message: "今天心情怎么样？",
            choices: [
                PetInteractionChoice("开心", "happy"),
                PetInteractionChoice("还可以", "okay"),
                PetInteractionChoice("不咋地", "low")
            ]
        ) { [weak self] choice in
            guard let self else { return }
            if let mood = choice?.value {
                interactions.recordMood(mood)
                let response: String
                switch mood {
                case "happy": response = "那就把这份开心多留一会儿。"
                case "low": response = "先不用硬撑，我在这儿陪你一会儿。"
                default: response = "平平稳稳也很好，慢慢来。"
                }
                showBubble(response)
            }
            finishInteraction()
        }
    }

    private func showContentInteraction(_ item: InteractionContentItem) {
        if item.type == "joke" {
            showInteraction(
                title: "冷笑话时间",
                message: item.prompt,
                choices: [PetInteractionChoice("看答案", "reveal", isPrimary: true)]
            ) { [weak self] choice in
                guard let self else { return }
                guard choice != nil else { finishInteraction(); return }
                interactions.recordJoke(contentId: item.id)
                showAnswer(title: "答案", message: formatAnswer(item), correct: nil)
            }
            return
        }

        if ["tip", "care"].contains(item.type) {
            let title = item.type == "tip" ? "生活小贴士" : "关心你一下"
            let button = item.type == "tip" ? "记下了" : "我知道了"
            showInteraction(
                title: title,
                message: "\(item.prompt)\n\n\(formatAnswer(item))",
                choices: [PetInteractionChoice(button, "acknowledge", isPrimary: true)]
            ) { [weak self] _ in self?.finishInteraction() }
            return
        }

        let title = switch item.type {
        case "math": "来道数学题"
        case "riddle": "脑筋急转弯"
        default: "趣味知识"
        }
        if !item.choices.isEmpty {
            let choices = item.choices.enumerated().map { index, value in
                PetInteractionChoice(value, String(index))
            }
            showInteraction(title: title, message: item.prompt, choices: choices) { [weak self] choice in
                guard let self else { return }
                guard let value = choice?.value, let index = Int(value), item.choices.indices.contains(index) else {
                    finishInteraction()
                    return
                }
                let correct = answersMatch(item.choices[index], item.answer)
                interactions.recordQuiz(contentId: item.id, correct: correct)
                showAnswer(
                    title: correct ? "答对了" : "答案揭晓",
                    message: formatAnswer(item),
                    correct: correct
                )
            }
            return
        }

        showInteraction(
            title: title,
            message: item.prompt,
            choices: [PetInteractionChoice("查看答案", "reveal", isPrimary: true)]
        ) { [weak self] choice in
            guard let self else { return }
            guard choice != nil else { finishInteraction(); return }
            showInteraction(
                title: "答案",
                message: formatAnswer(item),
                choices: [
                    PetInteractionChoice("答对了", "correct", isPrimary: true),
                    PetInteractionChoice("没答对", "wrong")
                ]
            ) { [weak self] result in
                guard let self else { return }
                if let result { interactions.recordQuiz(contentId: item.id, correct: result.value == "correct") }
                finishInteraction()
            }
        }
    }

    private func showAnswer(title: String, message: String, correct: Bool?) {
        showInteraction(
            title: title,
            message: message,
            choices: [PetInteractionChoice(correct == true ? "收下这分" : "知道了", "done", isPrimary: true)]
        ) { [weak self] _ in self?.finishInteraction() }
    }

    private func showInteraction(
        title: String,
        message: String,
        choices: [PetInteractionChoice],
        completion: @escaping (PetInteractionChoice?) -> Void
    ) {
        interactionPanel.present(
            title: title,
            message: message,
            choices: choices,
            relativeTo: window,
            completion: completion
        )
    }

    private func formatAnswer(_ item: InteractionContentItem) -> String {
        let answer = item.answer.trimmingCharacters(in: .whitespacesAndNewlines)
        let explanation = item.explanation.trimmingCharacters(in: .whitespacesAndNewlines)
        return explanation.isEmpty || explanation == answer ? answer : "\(answer)\n\(explanation)"
    }

    private func answersMatch(_ selected: String, _ answer: String) -> Bool {
        selected.trimmingCharacters(in: .whitespacesAndNewlines)
            .caseInsensitiveCompare(answer.trimmingCharacters(in: .whitespacesAndNewlines)) == .orderedSame
    }

    private func finishInteraction() {
        interactionActive = false
        restartInteractionTimer()
        if interactions.shouldFlush {
            Task { @MainActor [weak self] in try? await self?.interactions.flushEvents() }
        }
    }

    @discardableResult
    func randomizePet() -> Bool {
        guard theaterTask == nil, !interactionActive else { return false }
        guard let selected = petBag.next(from: petURLs, excluding: currentPetURL) else { return false }
        currentPetURL = selected
        petView.showPet(at: selected)
        showReaction("switch")
        return true
    }

    @discardableResult
    func startTheater() -> Bool {
        guard theaterTask == nil, !interactionActive else { return false }
        let scripts = settings.theaterScripts.isEmpty ? Self.builtInScripts : settings.theaterScripts
        guard let script = scripts.randomElement(), !script.scenes.isEmpty else { return false }
        let originalOrigin = window.frame.origin
        let companion = makeCompanionWindow()
        companionWindow = companion.window
        let companionView = companion.view
        positionCompanion(companion.window)
        let companionTarget = companion.window.frame.origin
        companion.window.setFrameOrigin(NSPoint(x: companionTarget.x, y: companionTarget.y - 28))
        companion.window.orderFrontRegardless()

        theaterTask = Task { @MainActor [weak self, weak companionView] in
            guard let self, let companionView else { return }
            await animatePair(
                window, to: originalOrigin,
                companion.window, to: companionTarget,
                duration: 0.45
            )
            for (index, scene) in script.scenes.enumerated() {
                guard !Task.isCancelled else { break }
                petView.showBubble(scene.main, duration: 4.4)
                try? await Task.sleep(for: .seconds(2.1))
                guard !Task.isCancelled else { break }
                companionView.showBubble(scene.companion, duration: 4.4)
                try? await Task.sleep(for: .seconds(1.1))
                guard !Task.isCancelled else { break }
                await performTheaterMotion(step: index, companion: companion.window)
                if index < script.scenes.count - 1 { try? await Task.sleep(for: .seconds(1.2)) }
            }
            try? await Task.sleep(for: .seconds(1.8))
            await animatePair(
                window, to: originalOrigin,
                companion.window, to: companionTarget,
                duration: 0.5
            )
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
                Task { @MainActor [weak self] in _ = self?.randomizePet() }
            }
            RunLoop.main.add(timer, forMode: .common)
            randomPetTimer = timer
        }
        if settings.theaterEnabled {
            let timer = Timer(timeInterval: TimeInterval(settings.theaterIntervalSeconds), repeats: true) { [weak self] _ in
                Task { @MainActor [weak self] in _ = self?.startTheater() }
            }
            RunLoop.main.add(timer, forMode: .common)
            theaterTimer = timer
        }
    }

    private func restartInteractionTimer() {
        interactionTimer?.invalidate()
        interactionTimer = nil
        guard settings.randomInteractionsEnabled else { return }
        let timer = Timer(
            timeInterval: InteractionRules.nextDelay(for: settings.interactionMode),
            repeats: false
        ) { [weak self] _ in
            Task { @MainActor [weak self] in await self?.presentRandomInteraction(manual: false) }
        }
        RunLoop.main.add(timer, forMode: .common)
        interactionTimer = timer
    }

    private func scheduleInteractionSync(after delay: TimeInterval) {
        if interactionSyncTask != nil {
            interactionSyncRequested = true
            return
        }
        interactionSyncTimer?.invalidate()
        let timer = Timer(timeInterval: delay, repeats: false) { [weak self] _ in
            Task { @MainActor [weak self] in self?.runInteractionSync() }
        }
        RunLoop.main.add(timer, forMode: .common)
        interactionSyncTimer = timer
    }

    private func runInteractionSync() {
        guard interactionSyncTask == nil else {
            interactionSyncRequested = true
            return
        }
        interactionSyncRequested = false
        interactionSyncTask = Task { @MainActor [weak self] in
            guard let self else { return }
            do {
                let profile = try await interactions.syncProfile(
                    localMode: settings.interactionMode,
                    localPromptsEnabled: settings.randomInteractionsEnabled
                )
                applyInteractionProfile(profile)
                try await interactions.flushEvents()
                _ = try await interactions.refill()
            } catch {
                // Periodic synchronization retries without interrupting the desktop pet.
            }
            interactionSyncTask = nil
            scheduleInteractionSync(after: interactionSyncRequested ? 2 : 15 * 60)
        }
    }

    private func applyInteractionProfile(_ profile: InteractionProfile) {
        let mode = InteractionRules.normalizeMode(profile.mode)
        let changed = settings.interactionMode != mode
            || settings.randomInteractionsEnabled != profile.promptsEnabled
        settings.interactionMode = mode
        settings.randomInteractionsEnabled = profile.promptsEnabled
        restartInteractionTimer()
        if changed { saveSettings() }
    }

    private func beginDrag(at point: NSPoint) {
        dragging = true
        velocity = .zero
        dragOffset = NSPoint(x: point.x - window.frame.origin.x, y: point.y - window.frame.origin.y)
        previousDragPoint = point
        previousDragTime = Date()
        if !interactionActive { showReaction("grab") }
    }

    private func continueDrag(to point: NSPoint) {
        guard dragging else { return }
        let now = Date()
        let elapsed = CGFloat(max(now.timeIntervalSince(previousDragTime), 1.0 / 240.0))
        velocity = CGVector(dx: (point.x - previousDragPoint.x) / elapsed / 60, dy: (point.y - previousDragPoint.y) / elapsed / 60)
        previousDragPoint = point
        previousDragTime = now
        window.setFrameOrigin(NSPoint(x: point.x - dragOffset.x, y: point.y - dragOffset.y))
        interactionPanel.reposition(relativeTo: window)
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
        guard window.isVisible, !dragging, theaterTask == nil, !interactionActive else { return }
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
        interactionPanel.updateLevel(window.level)
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
        let companionURL = petURLs.filter { $0 != currentPetURL }.randomElement() ?? currentPetURL
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

    private func performTheaterMotion(step: Int, companion: NSPanel) async {
        let mainOrigin = window.frame.origin
        let companionOrigin = companion.frame.origin
        switch step % 4 {
        case 0:
            await animatePair(
                window, to: NSPoint(x: mainOrigin.x, y: mainOrigin.y + 22),
                companion, to: NSPoint(x: companionOrigin.x, y: companionOrigin.y + 22),
                duration: 0.22
            )
            await animatePair(window, to: mainOrigin, companion, to: companionOrigin, duration: 0.24)
        case 1:
            await animatePair(window, to: companionOrigin, companion, to: mainOrigin, duration: 0.65)
        case 2:
            let direction: CGFloat = mainOrigin.x <= companionOrigin.x ? 1 : -1
            await animatePair(
                window, to: NSPoint(x: mainOrigin.x + 18 * direction, y: mainOrigin.y + 12),
                companion, to: NSPoint(x: companionOrigin.x - 18 * direction, y: companionOrigin.y - 8),
                duration: 0.25
            )
            await animatePair(
                window, to: NSPoint(x: mainOrigin.x - 12 * direction, y: mainOrigin.y - 6),
                companion, to: NSPoint(x: companionOrigin.x + 12 * direction, y: companionOrigin.y + 14),
                duration: 0.25
            )
            await animatePair(window, to: mainOrigin, companion, to: companionOrigin, duration: 0.25)
        default:
            await animatePair(window, to: companionOrigin, companion, to: mainOrigin, duration: 0.65)
        }
    }

    private func animatePair(
        _ first: NSWindow,
        to firstOrigin: NSPoint,
        _ second: NSWindow,
        to secondOrigin: NSPoint,
        duration: TimeInterval
    ) async {
        await withCheckedContinuation { continuation in
            NSAnimationContext.runAnimationGroup { context in
                context.duration = duration
                context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                first.animator().setFrameOrigin(firstOrigin)
                second.animator().setFrameOrigin(secondOrigin)
            } completionHandler: {
                continuation.resume()
            }
        }
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
