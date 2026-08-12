import AppKit

@MainActor
final class StickerWindowController: NSWindowController {
    var onDismiss: (() -> Void)?

    private struct StickerDefinition {
        let asset: String
        let label: String
        let color: NSColor
    }

    private static let stickers: [String: StickerDefinition] = [
        "heart": StickerDefinition(asset: "heart", label: "心心", color: NSColor(calibratedRed: 0.91, green: 0.36, blue: 0.44, alpha: 1)),
        "coffee": StickerDefinition(asset: "coffee", label: "咖啡", color: NSColor(calibratedRed: 0.67, green: 0.44, blue: 0.28, alpha: 1)),
        "catPaw": StickerDefinition(asset: "catPaw", label: "猫爪", color: NSColor(calibratedRed: 0.77, green: 0.49, blue: 0.70, alpha: 1)),
        "star": StickerDefinition(asset: "star", label: "星星", color: NSColor(calibratedRed: 0.91, green: 0.68, blue: 0.19, alpha: 1)),
        "cheer": StickerDefinition(asset: "cheer", label: "加油", color: NSColor(calibratedRed: 0.29, green: 0.61, blue: 0.73, alpha: 1)),
        "goodnight": StickerDefinition(asset: "goodnight", label: "晚安", color: NSColor(calibratedRed: 0.42, green: 0.46, blue: 0.75, alpha: 1))
    ]

    init(stickerID: String, senderName: String) {
        let sticker = Self.stickers[stickerID] ?? StickerDefinition(asset: "heart", label: "贴纸", color: NSColor.systemPink)
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 64, height: 64),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.title = "\(senderName) 发来的\(sticker.label)"
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .floating
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

        let container = NSVisualEffectView(frame: NSRect(x: 0, y: 0, width: 64, height: 64))
        container.material = .popover
        container.blendingMode = .withinWindow
        container.state = .active
        container.wantsLayer = true
        container.layer?.cornerRadius = 16
        container.layer?.borderWidth = 1
        container.layer?.borderColor = sticker.color.withAlphaComponent(0.65).cgColor

        let button = NSButton()
        button.translatesAutoresizingMaskIntoConstraints = false
        button.isBordered = false
        button.image = NSImage(contentsOfFile: Bundle.main.path(forResource: sticker.asset, ofType: "png", inDirectory: "Stickers") ?? "")
        button.imageScaling = .scaleProportionallyUpOrDown
        button.toolTip = "点击收起贴纸"
        button.target = nil
        container.addSubview(button)
        NSLayoutConstraint.activate([
            button.centerXAnchor.constraint(equalTo: container.centerXAnchor),
            button.centerYAnchor.constraint(equalTo: container.centerYAnchor),
            button.widthAnchor.constraint(equalToConstant: 42),
            button.heightAnchor.constraint(equalToConstant: 42)
        ])
        panel.contentView = container
        super.init(window: panel)
        button.target = self
        button.action = #selector(dismissAction)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    @objc private func dismissAction() {
        onDismiss?()
        close()
    }
}
