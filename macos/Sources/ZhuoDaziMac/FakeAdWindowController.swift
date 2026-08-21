import AppKit
import ApplicationServices
import CoreGraphics

@MainActor
final class FakeAdWindowController: NSObject, NSWindowDelegate {
    private static let fullSize = NSSize(width: 560, height: 360)
    private static let compactSize = NSSize(width: 440, height: 280)
    private let window: NSPanel
    private let canvas: FakeAdCanvasView
    private let chrome: NSPanel
    private let overlay: NSPanel
    private let actionPanel: NSPanel
    private let overlayTitle = NSTextField(labelWithString: "限时秒杀 · 仅剩 2 小时")
    private let overlaySubtitle = NSTextField(labelWithString: "爆款办公低至 3 折")
    private let actionButton = NSButton(title: "立即抢购", target: nil, action: nil)
    private let hint = NSTextField(wrappingLabelWithString: "请把抖音窗口拖到这里")
    private var watchTimer: Timer?
    private var douyinApplication: NSRunningApplication?
    private var douyinWindow: AXUIElement?
    private var originalFrame: CGRect?
    private var previousFrames: [String: CGRect] = [:]
    private var compact = false
    private var closing = false
    private var targetFrame = NSRect(origin: .zero, size: FakeAdWindowController.fullSize)

    private static let skins = [
        ("新客专享红包", "最高 88 元，今日名额有限", "马上领取"),
        ("免费领 7 天会员", "学习 / 影视 / 音乐任选", "马上领取"),
        ("限时秒杀 · 仅剩 2 小时", "爆款办公低至 3 折", "立即抢购")
    ]

    override init() {
        canvas = FakeAdCanvasView(frame: NSRect(origin: .zero, size: Self.fullSize))
        window = NSPanel(
            contentRect: NSRect(origin: .zero, size: Self.fullSize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        chrome = NSPanel(
            contentRect: NSRect(origin: .zero, size: NSSize(width: Self.fullSize.width, height: 28)),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        overlay = NSPanel(
            contentRect: NSRect(origin: .zero, size: NSSize(width: 300, height: 66)),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        actionPanel = NSPanel(
            contentRect: NSRect(origin: .zero, size: NSSize(width: 96, height: 28)),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        super.init()
        buildWindow()
        buildOverlay()
    }

    func show() {
        closing = false
        if douyinWindow == nil {
            if !window.isVisible { positionBottomRight() }
            window.orderFrontRegardless()
        } else {
            douyinApplication?.unhide()
            chrome.orderFrontRegardless()
        }
        overlay.orderFrontRegardless()
        actionPanel.orderFrontRegardless()
        startWatching()
        syncAttachedWindow()
    }

    func close() {
        guard !closing else { return }
        closing = true
        watchTimer?.invalidate()
        watchTimer = nil
        restoreAndCloseDouyin()
        chrome.orderOut(nil)
        overlay.orderOut(nil)
        actionPanel.orderOut(nil)
        self.window.orderOut(nil)
        previousFrames.removeAll()
        hint.isHidden = false
        hint.stringValue = "请把抖音窗口拖到这里"
        closing = false
    }

    private func buildWindow() {
        window.delegate = self
        window.title = "广告"
        window.backgroundColor = NSColor(calibratedWhite: 0.07, alpha: 1)
        window.isOpaque = true
        window.hasShadow = true
        window.level = .normal
        window.hidesOnDeactivate = false
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.contentView = canvas

        canvas.addSubview(makeTopBar(width: Self.fullSize.width))

        hint.textColor = .secondaryLabelColor
        hint.alignment = .center
        hint.font = .systemFont(ofSize: 14)
        hint.frame = NSRect(x: 40, y: 150, width: Self.fullSize.width - 80, height: 45)
        hint.autoresizingMask = [.width, .minYMargin, .maxYMargin]
        canvas.addSubview(hint)

        chrome.delegate = self
        chrome.backgroundColor = NSColor(calibratedWhite: 0.14, alpha: 1)
        chrome.isOpaque = true
        chrome.hasShadow = true
        chrome.level = .floating
        chrome.hidesOnDeactivate = false
        chrome.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        chrome.contentView = makeTopBar(width: Self.fullSize.width)
    }

    private func buildOverlay() {
        overlay.backgroundColor = NSColor(calibratedWhite: 0.03, alpha: 0.82)
        overlay.isOpaque = false
        overlay.hasShadow = true
        overlay.level = .floating
        overlay.ignoresMouseEvents = true
        overlay.hidesOnDeactivate = false
        overlay.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

        let content = NSView(frame: NSRect(x: 0, y: 0, width: 300, height: 66))
        overlayTitle.textColor = .white
        overlayTitle.font = .systemFont(ofSize: 16, weight: .bold)
        overlayTitle.frame = NSRect(x: 8, y: 34, width: 284, height: 24)
        overlaySubtitle.textColor = .white
        overlaySubtitle.font = .systemFont(ofSize: 11)
        overlaySubtitle.frame = NSRect(x: 8, y: 10, width: 284, height: 18)
        content.addSubview(overlayTitle)
        content.addSubview(overlaySubtitle)
        overlay.contentView = content

        actionPanel.backgroundColor = .clear
        actionPanel.isOpaque = false
        actionPanel.hasShadow = true
        actionPanel.level = .floating
        actionPanel.hidesOnDeactivate = false
        actionPanel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        actionButton.target = self
        actionButton.action = #selector(changeSkinAction)
        actionButton.bezelStyle = .rounded
        actionButton.font = .systemFont(ofSize: 12, weight: .semibold)
        actionButton.frame = NSRect(x: 0, y: 0, width: 96, height: 28)
        actionPanel.contentView = actionButton
        applyRandomSkin()
    }

    private func makeTopBar(width: CGFloat) -> NSView {
        let drag = AdDragView(frame: NSRect(x: 0, y: 0, width: width, height: 28))
        drag.autoresizingMask = [.width]

        let tag = NSTextField(labelWithString: "广告")
        tag.textColor = .white
        tag.font = .systemFont(ofSize: 12)
        tag.frame = NSRect(x: 10, y: 6, width: 42, height: 18)
        drag.addSubview(tag)

        let close = makeChromeButton("xmark", action: #selector(closeAction), toolTip: "关闭")
        let hide = makeChromeButton("minus", action: #selector(hideAction), toolTip: "隐藏")
        let resize = makeChromeButton("arrow.down.right.and.arrow.up.left", action: #selector(toggleSizeAction), toolTip: "缩小 / 恢复")
        let move = AdMoveButton(frame: .zero)
        move.image = NSImage(systemSymbolName: "arrow.up.and.down.and.arrow.left.and.right", accessibilityDescription: "拖动")
        move.isBordered = false
        move.contentTintColor = .white
        move.toolTip = "按住拖动"
        for (index, button) in [close, hide, resize, move].enumerated() {
            button.frame = NSRect(x: width - CGFloat(index + 1) * 32, y: 0, width: 32, height: 28)
            button.autoresizingMask = [.minXMargin]
            drag.addSubview(button)
        }
        return drag
    }

    private func makeChromeButton(_ symbol: String, action: Selector, toolTip: String) -> NSButton {
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: toolTip) ?? NSImage()
        let button = NSButton(image: image, target: self, action: action)
        button.isBordered = false
        button.contentTintColor = .white
        button.toolTip = toolTip
        return button
    }

    private func positionBottomRight() {
        guard let screen = NSScreen.main?.visibleFrame else { return }
        let size = compact ? Self.compactSize : Self.fullSize
        targetFrame = NSRect(
            x: screen.maxX - size.width - 16,
            y: screen.minY + 16,
            width: size.width,
            height: size.height
        )
        window.setFrame(targetFrame, display: false)
        positionOverlay()
    }

    private func startWatching() {
        guard watchTimer == nil else { return }
        watchTimer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in self?.watchForDouyin() }
        }
        RunLoop.main.add(watchTimer!, forMode: .common)
        let promptKey = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        if !AXIsProcessTrustedWithOptions([promptKey: true] as CFDictionary) {
            hint.stringValue = "请在系统设置中允许桌搭子使用辅助功能"
        }
    }

    private func watchForDouyin() {
        guard !closing else { return }
        if let douyinWindow {
            if douyinApplication?.isTerminated == true || axFrame(douyinWindow) == nil {
                self.douyinWindow = nil
                douyinApplication = nil
                originalFrame = nil
                window.orderFrontRegardless()
                chrome.orderOut(nil)
                hint.isHidden = false
                hint.stringValue = "请重新打开抖音并将窗口拖到这里"
                return
            }
            syncAttachedWindow()
            return
        }
        guard AXIsProcessTrusted() else { return }

        let videoFrame = screenVideoFrame()
        for (application, candidate, key) in douyinWindows() {
            guard let frame = axFrame(candidate) else { continue }
            let previous = previousFrames[key]
            previousFrames[key] = frame
            guard let previous, previous != frame else { continue }
            guard frame.intersection(videoFrame).width * frame.intersection(videoFrame).height >= videoFrame.width * videoFrame.height * 0.2 else { continue }
            attach(application: application, window: candidate, frame: frame)
            return
        }
    }

    private func douyinWindows() -> [(NSRunningApplication, AXUIElement, String)] {
        let applications = NSWorkspace.shared.runningApplications.filter { application in
            let name = (application.localizedName ?? "").lowercased()
            let bundle = (application.bundleIdentifier ?? "").lowercased()
            return name.contains("抖音") || name.contains("douyin") || bundle.contains("douyin")
        }
        return applications.flatMap { application -> [(NSRunningApplication, AXUIElement, String)] in
            let element = AXUIElementCreateApplication(application.processIdentifier)
            guard let windows: [AXUIElement] = axValue(element, kAXWindowsAttribute as CFString) else { return [] }
            return windows.enumerated().filter { axFrame($0.element) != nil }.map { (application, $0.element, "\(application.processIdentifier)-\($0.offset)") }
        }
    }

    private func attach(application: NSRunningApplication, window element: AXUIElement, frame: CGRect) {
        douyinApplication = application
        douyinWindow = element
        originalFrame = frame
        hint.isHidden = true
        window.orderOut(nil)
        positionChrome()
        chrome.orderFrontRegardless()
        syncAttachedWindow()
    }

    private func syncAttachedWindow() {
        guard let douyinWindow, axFrame(douyinWindow) != nil else { return }
        let frame = screenVideoFrame()
        setAXFrame(frame, for: douyinWindow)
        positionOverlay()
    }

    private func restoreAndCloseDouyin() {
        if let douyinWindow, let originalFrame { setAXFrame(originalFrame, for: douyinWindow) }
        douyinApplication?.terminate()
        douyinWindow = nil
        douyinApplication = nil
        originalFrame = nil
    }

    private func screenVideoFrame() -> CGRect {
        let frame = targetFrame
        return CGRect(x: frame.minX, y: frame.minY, width: frame.width, height: max(1, frame.height - 28))
    }

    private func positionOverlay() {
        let frame = targetFrame
        overlay.setFrameOrigin(NSPoint(x: frame.minX + 10, y: frame.maxY - 34 - overlay.frame.height))
        actionPanel.setFrameOrigin(NSPoint(x: frame.midX - actionPanel.frame.width / 2, y: frame.minY + 12))
        if overlay.isVisible { overlay.orderFrontRegardless() }
        if actionPanel.isVisible { actionPanel.orderFrontRegardless() }
    }

    private func positionChrome() {
        chrome.setFrame(NSRect(x: targetFrame.minX, y: targetFrame.maxY - 28, width: targetFrame.width, height: 28), display: true)
    }

    private func setAXFrame(_ frame: CGRect, for element: AXUIElement) {
        var position = CGPoint(x: frame.minX, y: screenTop - frame.maxY)
        var size = frame.size
        guard let positionValue = AXValueCreate(.cgPoint, &position),
              let sizeValue = AXValueCreate(.cgSize, &size) else { return }
        AXUIElementSetAttributeValue(element, kAXPositionAttribute as CFString, positionValue)
        AXUIElementSetAttributeValue(element, kAXSizeAttribute as CFString, sizeValue)
    }

    private func axFrame(_ element: AXUIElement) -> CGRect? {
        guard let position: AXValue = axValue(element, kAXPositionAttribute as CFString),
              let size: AXValue = axValue(element, kAXSizeAttribute as CFString) else { return nil }
        var point = CGPoint.zero
        var dimensions = CGSize.zero
        guard AXValueGetValue(position, .cgPoint, &point), AXValueGetValue(size, .cgSize, &dimensions) else { return nil }
        return CGRect(x: point.x, y: screenTop - point.y - dimensions.height, width: dimensions.width, height: dimensions.height)
    }

    private var screenTop: CGFloat {
        NSScreen.screens.first?.frame.maxY ?? 0
    }

    private func axValue<T>(_ element: AXUIElement, _ attribute: CFString) -> T? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute, &value) == .success else { return nil }
        return value as? T
    }

    @objc private func closeAction() { close() }

    @objc private func hideAction() {
        chrome.orderOut(nil)
        overlay.orderOut(nil)
        actionPanel.orderOut(nil)
        window.orderOut(nil)
        douyinApplication?.hide()
    }

    @objc private func toggleSizeAction() {
        let right = targetFrame.maxX
        let bottom = targetFrame.minY
        compact.toggle()
        let size = compact ? Self.compactSize : Self.fullSize
        targetFrame = NSRect(x: right - size.width, y: bottom, width: size.width, height: size.height)
        window.setFrame(targetFrame, display: true, animate: douyinWindow == nil)
        positionChrome()
        syncAttachedWindow()
    }

    @objc private func changeSkinAction() { applyRandomSkin() }

    private func applyRandomSkin() {
        let skin = Self.skins.randomElement() ?? Self.skins[0]
        overlayTitle.stringValue = skin.0
        overlaySubtitle.stringValue = skin.1
        actionButton.title = skin.2
    }

    func windowDidMove(_ notification: Notification) {
        guard !closing, let moved = notification.object as? NSWindow else { return }
        if moved === window {
            targetFrame = window.frame
        } else if moved === chrome {
            targetFrame.origin = NSPoint(x: chrome.frame.minX, y: chrome.frame.minY - targetFrame.height + 28)
            window.setFrame(targetFrame, display: false)
        }
        positionOverlay()
        if douyinWindow != nil { syncAttachedWindow() }
    }
}

private final class FakeAdCanvasView: NSView {
    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedWhite: 0.07, alpha: 1).setFill()
        dirtyRect.fill()
        NSColor(calibratedWhite: 0.14, alpha: 1).setFill()
        NSRect(x: 0, y: 0, width: bounds.width, height: 28).fill()
    }
}

private final class AdDragView: NSView {
    override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
}

private final class AdMoveButton: NSButton {
    override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
}
