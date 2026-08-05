import AppKit

struct PetInteractionChoice {
    let label: String
    let value: String
    let isPrimary: Bool

    init(_ label: String, _ value: String, isPrimary: Bool = false) {
        self.label = label
        self.value = value
        self.isPrimary = isPrimary
    }
}

private final class InteractionChoiceButton: NSButton {
    var choice: PetInteractionChoice?
}

private final class FlippedDocumentView: NSView {
    override var isFlipped: Bool { true }
}

final class PetInteractionPanelController: NSWindowController {
    private var completion: ((PetInteractionChoice?) -> Void)?

    init() {
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 380, height: 220),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.backgroundColor = .windowBackgroundColor
        panel.isOpaque = true
        panel.hasShadow = true
        panel.hidesOnDeactivate = false
        panel.isFloatingPanel = true
        panel.becomesKeyOnlyIfNeeded = true
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        super.init(window: panel)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func present(
        title: String,
        message: String,
        choices: [PetInteractionChoice],
        relativeTo parent: NSWindow,
        completion: @escaping (PetInteractionChoice?) -> Void
    ) {
        dismiss()
        self.completion = completion
        guard let panel = window as? NSPanel else { return }

        let content = NSView()
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.translatesAutoresizingMaskIntoConstraints = false
        content.addSubview(stack)

        let titleLabel = NSTextField(labelWithString: title)
        titleLabel.font = .systemFont(ofSize: 12, weight: .semibold)
        titleLabel.textColor = .systemGreen
        let closeImage = NSImage(systemSymbolName: "xmark", accessibilityDescription: "稍后再说") ?? NSImage()
        let close = NSButton(image: closeImage, target: self, action: #selector(closePanel))
        close.isBordered = false
        close.toolTip = "稍后再说"
        close.widthAnchor.constraint(equalToConstant: 24).isActive = true
        close.heightAnchor.constraint(equalToConstant: 24).isActive = true
        let header = NSStackView(views: [titleLabel, NSView(), close])
        header.orientation = .horizontal
        header.alignment = .centerY
        header.spacing = 8
        header.widthAnchor.constraint(equalToConstant: 348).isActive = true
        stack.addArrangedSubview(header)

        let messageLabel = NSTextField(wrappingLabelWithString: message)
        messageLabel.font = .systemFont(ofSize: 13)
        messageLabel.maximumNumberOfLines = 0
        messageLabel.lineBreakMode = .byWordWrapping

        let messageWidth: CGFloat = 348
        let measurementBounds = NSRect(
            x: 0,
            y: 0,
            width: messageWidth,
            height: CGFloat.greatestFiniteMagnitude
        )
        let naturalMessageHeight = max(
            20,
            ceil(messageLabel.cell?.cellSize(forBounds: measurementBounds).height ?? 20)
        )
        let rowCount = (choices.count + 1) / 2
        let fixedContentHeight = CGFloat(62 + rowCount * 52)
        let screenBounds = parent.screen?.visibleFrame ?? NSScreen.main?.visibleFrame
            ?? NSRect(x: 0, y: 0, width: 1_200, height: 800)
        let maximumPanelHeight = max(220, screenBounds.height - 24)
        let visibleMessageHeight = min(
            naturalMessageHeight,
            max(80, maximumPanelHeight - fixedContentHeight)
        )

        let messageDocument = FlippedDocumentView(
            frame: NSRect(x: 0, y: 0, width: messageWidth, height: naturalMessageHeight)
        )
        messageLabel.frame = messageDocument.bounds
        messageLabel.autoresizingMask = [.width]
        messageDocument.addSubview(messageLabel)

        let messageScroll = NSScrollView()
        messageScroll.drawsBackground = false
        messageScroll.borderType = .noBorder
        messageScroll.hasHorizontalScroller = false
        messageScroll.hasVerticalScroller = naturalMessageHeight > visibleMessageHeight
        messageScroll.autohidesScrollers = true
        messageScroll.documentView = messageDocument
        messageScroll.widthAnchor.constraint(equalToConstant: messageWidth).isActive = true
        messageScroll.heightAnchor.constraint(equalToConstant: visibleMessageHeight).isActive = true
        stack.addArrangedSubview(messageScroll)

        for start in stride(from: 0, to: choices.count, by: 2) {
            let rowChoices = Array(choices[start..<min(start + 2, choices.count)])
            let buttons = rowChoices.map(makeButton)
            if buttons.count == 1 { buttons[0].widthAnchor.constraint(equalToConstant: 348).isActive = true }
            let row = NSStackView(views: buttons)
            row.orientation = .horizontal
            row.alignment = .centerY
            row.distribution = .fillEqually
            row.spacing = 8
            row.widthAnchor.constraint(equalToConstant: 348).isActive = true
            stack.addArrangedSubview(row)
        }

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 16),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -16),
            stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 14),
            stack.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -14)
        ])
        let panelHeight = min(maximumPanelHeight, fixedContentHeight + visibleMessageHeight)
        panel.setContentSize(NSSize(width: 380, height: panelHeight))
        panel.contentView = content
        panel.level = parent.level
        position(panel, relativeTo: parent)
        panel.orderFrontRegardless()
    }

    func updateLevel(_ level: NSWindow.Level) { window?.level = level }

    func reposition(relativeTo parent: NSWindow) {
        guard let panel = window as? NSPanel, panel.isVisible else { return }
        position(panel, relativeTo: parent)
    }

    func dismiss(notifying: Bool = true) {
        guard completion != nil || window?.isVisible == true else { return }
        let callback = completion
        completion = nil
        window?.orderOut(nil)
        if notifying { callback?(nil) }
    }

    private func makeButton(_ choice: PetInteractionChoice) -> NSButton {
        let button = InteractionChoiceButton(title: choice.label, target: self, action: #selector(selectChoice(_:)))
        button.choice = choice
        button.font = .systemFont(ofSize: 12, weight: choice.isPrimary ? .semibold : .regular)
        button.bezelStyle = .rounded
        button.toolTip = choice.label
        button.cell?.wraps = true
        button.cell?.lineBreakMode = .byWordWrapping
        button.heightAnchor.constraint(equalToConstant: 42).isActive = true
        if choice.isPrimary {
            button.bezelColor = .systemGreen
            button.contentTintColor = .white
        }
        return button
    }

    private func position(_ panel: NSPanel, relativeTo parent: NSWindow) {
        let bounds = parent.screen?.visibleFrame ?? NSScreen.main?.visibleFrame
            ?? NSRect(x: 0, y: 0, width: 1_200, height: 800)
        var origin = NSPoint(
            x: parent.frame.midX - panel.frame.width / 2,
            y: parent.frame.maxY + 8
        )
        if origin.y + panel.frame.height > bounds.maxY {
            origin.y = parent.frame.minY - panel.frame.height - 8
        }
        origin.x = max(bounds.minX, min(origin.x, bounds.maxX - panel.frame.width))
        origin.y = max(bounds.minY, min(origin.y, bounds.maxY - panel.frame.height))
        panel.setFrameOrigin(origin)
    }

    @objc private func selectChoice(_ sender: InteractionChoiceButton) {
        let callback = completion
        completion = nil
        window?.orderOut(nil)
        callback?(sender.choice)
    }

    @objc private func closePanel() { dismiss() }
}
