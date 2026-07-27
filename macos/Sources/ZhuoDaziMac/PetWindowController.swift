import AppKit
import ZhuoDaziCore

final class PetWindowController {
    var mouseInteractionEnabled = true
    var randomMovementEnabled = true
    var randomPetEnabled = true {
        didSet { restartRandomPetTimer() }
    }
    var isVisible: Bool { window.isVisible }
    var canRandomizePet: Bool { petURLs.count > 1 }

    private let size = NSSize(width: 220, height: 250)
    private let window: NSPanel
    private let petView: PetCanvasView
    private let petBag = RandomBag<URL>()
    private var petURLs: [URL] = []
    private var currentPetURL: URL?
    private var timer: Timer?
    private var randomPetTimer: Timer?
    private var velocity = CGVector.zero
    private var dragging = false
    private var dragOffset = NSPoint.zero
    private var previousDragPoint = NSPoint.zero
    private var previousDragTime = Date()
    private var nextWanderDecision = Date()

    init() {
        let visibleFrame = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1200, height: 800)
        let origin = NSPoint(
            x: visibleFrame.maxX - size.width - 28,
            y: visibleFrame.minY + 24
        )
        window = NSPanel(
            contentRect: NSRect(origin: origin, size: size),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        petView = PetCanvasView(frame: NSRect(origin: .zero, size: size))

        window.backgroundColor = .clear
        window.isOpaque = false
        window.hasShadow = false
        window.isFloatingPanel = true
        window.hidesOnDeactivate = false
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.contentView = petView
        window.acceptsMouseMovedEvents = true

        petView.dragBegan = { [weak self] point in self?.beginDrag(at: point) }
        petView.dragMoved = { [weak self] point in self?.continueDrag(to: point) }
        petView.dragEnded = { [weak self] point in self?.endDrag(at: point) }
        petView.clicked = { [weak self] in self?.petView.showBubble("我在这里。") }

        petURLs = Self.scanPetURLs()
        if let initialPet = petBag.next(from: petURLs) {
            currentPetURL = initialPet
            petView.showPet(at: initialPet)
        }

        let timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in
            self?.tick()
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
        restartRandomPetTimer()
    }

    deinit {
        timer?.invalidate()
        randomPetTimer?.invalidate()
    }

    func show() {
        window.orderFrontRegardless()
    }

    func hide() {
        window.orderOut(nil)
    }

    @discardableResult
    func randomizePet() -> Bool {
        guard let selected = petBag.next(from: petURLs, excluding: currentPetURL) else { return false }
        currentPetURL = selected
        petView.showPet(at: selected)
        petView.showBubble("换班完成，新选手登场。")
        return true
    }

    private func restartRandomPetTimer() {
        randomPetTimer?.invalidate()
        randomPetTimer = nil
        guard randomPetEnabled, canRandomizePet else { return }

        let timer = Timer(timeInterval: 300, repeats: true) { [weak self] _ in
            self?.randomizePet()
        }
        RunLoop.main.add(timer, forMode: .common)
        randomPetTimer = timer
    }

    private static func scanPetURLs() -> [URL] {
        let sourceRoot = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
        let bundledPets = Bundle.main.resourceURL?.appendingPathComponent("Pets", isDirectory: true)
        let bundledLibrary = bundledPets?.appendingPathComponent("yuexinmiao", isDirectory: true)
        let sharedDevelopmentLibrary = sourceRoot
            .deletingLastPathComponent()
            .appendingPathComponent("windows/assets/pet-libraries/yuexinmiao", isDirectory: true)
        let developmentFallback = sourceRoot.appendingPathComponent("Resources/Pets", isDirectory: true)
        let candidates = [bundledLibrary, sharedDevelopmentLibrary, bundledPets, developmentFallback]
            .compactMap { $0 }
        guard let directory = candidates.first(where: { FileManager.default.fileExists(atPath: $0.path) }) else {
            return []
        }

        let urls = (try? FileManager.default.contentsOfDirectory(
            at: directory,
            includingPropertiesForKeys: nil,
            options: [.skipsHiddenFiles]
        )) ?? []
        return urls
            .filter { $0.pathExtension.caseInsensitiveCompare("gif") == .orderedSame }
            .sorted { $0.path.localizedCaseInsensitiveCompare($1.path) == .orderedAscending }
    }

    private func beginDrag(at point: NSPoint) {
        dragging = true
        velocity = .zero
        dragOffset = NSPoint(x: point.x - window.frame.origin.x, y: point.y - window.frame.origin.y)
        previousDragPoint = point
        previousDragTime = Date()
        petView.showBubble("慢一点，我要起飞了。")
    }

    private func continueDrag(to point: NSPoint) {
        guard dragging else { return }
        let now = Date()
        let elapsed = CGFloat(max(now.timeIntervalSince(previousDragTime), 1.0 / 240.0))
        velocity = CGVector(
            dx: (point.x - previousDragPoint.x) / elapsed / 60.0,
            dy: (point.y - previousDragPoint.y) / elapsed / 60.0
        )
        previousDragPoint = point
        previousDragTime = now
        window.setFrameOrigin(NSPoint(x: point.x - dragOffset.x, y: point.y - dragOffset.y))
    }

    private func endDrag(at point: NSPoint) {
        dragging = false
        velocity.dx = max(-34, min(34, velocity.dx))
        velocity.dy = max(-34, min(34, velocity.dy))
    }

    private func tick() {
        guard window.isVisible, !dragging else { return }
        var origin = window.frame.origin
        let center = NSPoint(x: origin.x + size.width / 2, y: origin.y + size.height / 2)

        if mouseInteractionEnabled {
            let mouse = NSEvent.mouseLocation
            let deltaX = mouse.x - center.x
            let deltaY = mouse.y - center.y
            let distance = hypot(deltaX, deltaY)
            if distance > 90, distance < 520 {
                velocity.dx += deltaX / distance * 0.075
                velocity.dy += deltaY / distance * 0.075
            }
        }

        if randomMovementEnabled, Date() >= nextWanderDecision {
            velocity.dx += CGFloat.random(in: -1.8...1.8)
            velocity.dy += CGFloat.random(in: -1.1...1.1)
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
        } else if origin.x + size.width > bounds.maxX {
            origin.x = bounds.maxX - size.width
            velocity.dx = -abs(velocity.dx) * 0.78
        }
        if origin.y < bounds.minY {
            origin.y = bounds.minY
            velocity.dy = abs(velocity.dy) * 0.78
        } else if origin.y + size.height > bounds.maxY {
            origin.y = bounds.maxY - size.height
            velocity.dy = -abs(velocity.dy) * 0.78
        }

        window.setFrameOrigin(origin)
    }
}
