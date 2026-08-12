import AppKit

@MainActor
final class StickerWindowController: NSWindowController {
    var onDismiss: (() -> Void)?

    private static let stickers: [String: (emoji: String, label: String)] = [
        "heart": ("❤️", "心心"),
        "coffee": ("☕", "咖啡"),
        "catPaw": ("🐾", "猫爪"),
        "star": ("⭐", "星星"),
        "cheer": ("💪", "加油"),
        "goodnight": ("🌙", "晚安")
    ]

    init(stickerID: String, senderName: String) {
        let sticker = Self.stickers[stickerID] ?? ("💌", "贴纸")
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 96, height: 96),
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

        let container = NSVisualEffectView(frame: NSRect(x: 0, y: 0, width: 96, height: 96))
        container.material = .popover
        container.blendingMode = .withinWindow
        container.state = .active
        container.wantsLayer = true
        container.layer?.cornerRadius = 18
        container.layer?.borderWidth = 1
        container.layer?.borderColor = NSColor(calibratedRed: 0.86, green: 0.76, blue: 0.68, alpha: 0.8).cgColor

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.spacing = 0
        stack.translatesAutoresizingMaskIntoConstraints = false
        let button = NSButton(title: sticker.emoji, target: nil, action: nil)
        button.isBordered = false
        button.font = NSFont(name: "Apple Color Emoji", size: 42) ?? .systemFont(ofSize: 42)
        button.toolTip = "点击收起贴纸"
        button.target = nil
        let label = NSTextField(labelWithString: senderName)
        label.font = .systemFont(ofSize: 10)
        label.textColor = .secondaryLabelColor
        label.maximumNumberOfLines = 1
        label.lineBreakMode = .byTruncatingTail
        label.alignment = .center
        label.maximumNumberOfLines = 1
        stack.addArrangedSubview(button)
        stack.addArrangedSubview(label)
        container.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: container.leadingAnchor, constant: 6),
            stack.trailingAnchor.constraint(equalTo: container.trailingAnchor, constant: -6),
            stack.topAnchor.constraint(equalTo: container.topAnchor, constant: 4),
            stack.bottomAnchor.constraint(equalTo: container.bottomAnchor, constant: -4),
            button.widthAnchor.constraint(equalToConstant: 72),
            button.heightAnchor.constraint(equalToConstant: 62)
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
