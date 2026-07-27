import AppKit

@MainActor
final class InvitationWindowController: NSObject, NSWindowDelegate {
    private let window: NSWindow
    private let input = NSTextField(string: "")
    private let statusLabel = NSTextField(labelWithString: "")
    private let contactButton = NSButton(title: "没有邀请码？联系作者获取  ›", target: nil, action: nil)
    private let contactBox = NSBox()
    private var result: String?

    init(required: Bool) {
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 500, height: 350),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        super.init()
        window.title = required ? "欢迎带桌搭子回家" : "更换邀请码"
        window.isReleasedWhenClosed = false
        window.delegate = self
        buildInterface(required: required)
        window.center()
    }

    func runModal() -> String? {
        result = nil
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        window.makeFirstResponder(input)
        NSApp.runModal(for: window)
        window.orderOut(nil)
        return result
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        result = nil
        NSApp.stopModal()
        return true
    }

    private func buildInterface(required: Bool) {
        let root = NSView()
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 12
        stack.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 28),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -28),
            stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 24),
            stack.bottomAnchor.constraint(lessThanOrEqualTo: root.bottomAnchor, constant: -22)
        ])
        window.contentView = root

        let heading = NSTextField(labelWithString: required ? "欢迎带桌搭子回家" : "更换邀请码")
        heading.font = .systemFont(ofSize: 23, weight: .bold)
        stack.addArrangedSubview(heading)
        let subtitle = NSTextField(labelWithString: required ? "输入邀请码，开始今天的陪伴" : "输入新的邀请码以更新此设备的绑定")
        subtitle.textColor = .secondaryLabelColor
        stack.addArrangedSubview(subtitle)

        let inputLabel = NSTextField(labelWithString: "6 位邀请码")
        inputLabel.font = .systemFont(ofSize: 13, weight: .semibold)
        inputLabel.setContentHuggingPriority(.required, for: .vertical)
        stack.setCustomSpacing(24, after: subtitle)
        stack.addArrangedSubview(inputLabel)
        input.placeholderString = "请输入邀请码"
        input.font = .monospacedSystemFont(ofSize: 22, weight: .semibold)
        input.alignment = .center
        input.widthAnchor.constraint(equalToConstant: 444).isActive = true
        input.heightAnchor.constraint(equalToConstant: 42).isActive = true
        stack.addArrangedSubview(input)

        statusLabel.textColor = .systemRed
        statusLabel.font = .systemFont(ofSize: 12)
        statusLabel.stringValue = " "
        stack.addArrangedSubview(statusLabel)

        contactButton.target = self
        contactButton.action = #selector(toggleContact)
        contactButton.isBordered = false
        contactButton.contentTintColor = .controlAccentColor
        contactButton.font = .systemFont(ofSize: 13, weight: .semibold)
        stack.addArrangedSubview(contactButton)

        configureContactBox()
        contactBox.isHidden = true
        stack.addArrangedSubview(contactBox)

        let cancel = NSButton(title: required ? "退出" : "取消", target: self, action: #selector(cancelPrompt))
        let submit = NSButton(title: "开始使用", target: self, action: #selector(submitPrompt))
        submit.keyEquivalent = "\r"
        submit.bezelColor = .controlAccentColor
        let buttons = NSStackView(views: [cancel, submit])
        buttons.orientation = .horizontal
        buttons.spacing = 8
        buttons.alignment = .centerY
        let buttonContainer = NSView()
        buttonContainer.translatesAutoresizingMaskIntoConstraints = false
        buttonContainer.addSubview(buttons)
        buttons.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            buttonContainer.widthAnchor.constraint(equalToConstant: 444),
            buttonContainer.heightAnchor.constraint(equalToConstant: 34),
            buttons.trailingAnchor.constraint(equalTo: buttonContainer.trailingAnchor),
            buttons.centerYAnchor.constraint(equalTo: buttonContainer.centerYAnchor)
        ])
        stack.setCustomSpacing(18, after: contactBox)
        stack.addArrangedSubview(buttonContainer)
    }

    private func configureContactBox() {
        contactBox.boxType = .custom
        contactBox.borderType = .lineBorder
        contactBox.borderColor = .separatorColor
        contactBox.fillColor = .controlBackgroundColor
        contactBox.cornerRadius = 8
        contactBox.contentViewMargins = NSSize(width: 14, height: 14)
        contactBox.widthAnchor.constraint(equalToConstant: 444).isActive = true
        contactBox.heightAnchor.constraint(equalToConstant: 230).isActive = true

        let qrView = NSImageView()
        qrView.imageScaling = .scaleProportionallyUpOrDown
        qrView.image = Self.contactImage()
        qrView.widthAnchor.constraint(equalToConstant: 200).isActive = true
        qrView.heightAnchor.constraint(equalToConstant: 200).isActive = true

        let title = NSTextField(labelWithString: "扫码添加作者微信")
        title.font = .systemFont(ofSize: 15, weight: .semibold)
        let note = wrappingLabel("备注「桌搭子」，即可联系获取邀请码。")
        let accountTitle = NSTextField(labelWithString: "微信号")
        accountTitle.font = .systemFont(ofSize: 11)
        accountTitle.textColor = .secondaryLabelColor
        let account = NSTextField(labelWithString: "wcl_lcw627")
        account.font = .monospacedSystemFont(ofSize: 14, weight: .semibold)
        account.isSelectable = true
        let support = wrappingLabel("如果桌搭子刚好给你带来一点陪伴，也欢迎请作者喝一杯库迪，支持后续维护与更新。")

        let copy = NSStackView(views: [title, note, accountTitle, account, support])
        copy.orientation = .vertical
        copy.alignment = .leading
        copy.spacing = 7
        copy.widthAnchor.constraint(equalToConstant: 194).isActive = true
        let row = NSStackView(views: [qrView, copy])
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 16
        contactBox.contentView = row
    }

    private func wrappingLabel(_ text: String) -> NSTextField {
        let label = NSTextField(wrappingLabelWithString: text)
        label.font = .systemFont(ofSize: 12)
        label.textColor = .secondaryLabelColor
        label.maximumNumberOfLines = 4
        label.lineBreakMode = .byWordWrapping
        return label
    }

    @objc private func toggleContact() {
        let show = contactBox.isHidden
        contactBox.isHidden = !show
        contactButton.title = show ? "收起联系方式  ‹" : "没有邀请码？联系作者获取  ›"
        window.setContentSize(NSSize(width: 500, height: show ? 590 : 350))
        window.center()
    }

    @objc private func submitPrompt() {
        let allowed = Set("ABCDEFGHJKLMNPQRSTUVWXYZ23456789")
        let normalized = input.stringValue.uppercased().filter { allowed.contains($0) }
        input.stringValue = String(normalized.prefix(6))
        guard input.stringValue.count == 6 else {
            statusLabel.stringValue = "请输入完整的 6 位邀请码。"
            window.makeFirstResponder(input)
            return
        }
        result = input.stringValue
        NSApp.stopModal()
    }

    @objc private func cancelPrompt() {
        result = nil
        NSApp.stopModal()
    }

    private static func contactImage() -> NSImage? {
        if let bundled = Bundle.main.url(forResource: "contact-author-wechat", withExtension: "png") {
            return NSImage(contentsOf: bundled)
        }
        let macRoot = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
        let developmentAsset = macRoot.deletingLastPathComponent()
            .appendingPathComponent("windows/assets/contact-author-wechat.png")
        return NSImage(contentsOf: developmentAsset)
    }
}
