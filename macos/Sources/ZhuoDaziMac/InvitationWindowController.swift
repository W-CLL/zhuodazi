import AppKit

@MainActor
final class InvitationWindowController: NSObject, NSWindowDelegate {
    private let window: NSWindow
    private let input = NSTextField(string: "")
    private let statusLabel = NSTextField(labelWithString: "")
    private let contactButton = NSButton(title: "没有激活码？看看怎么支持作者  ›", target: nil, action: nil)
    private let contactBox = NSBox()
    private var result: String?

    init(required: Bool, statusMessage: String? = nil) {
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 500, height: 395),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        super.init()
        window.title = required ? "解锁桌搭子完整功能" : "更换激活码"
        window.isReleasedWhenClosed = false
        window.delegate = self
        buildInterface(required: required, statusMessage: statusMessage)
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

    private func buildInterface(required: Bool, statusMessage: String?) {
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

        let heading = NSTextField(labelWithString: required ? "解锁桌搭子完整功能" : "更换激活码")
        heading.font = .systemFont(ofSize: 23, weight: .bold)
        stack.addArrangedSubview(heading)
        let subtitle = NSTextField(wrappingLabelWithString: required
            ? "基础陪伴永久免费；激活后解锁小剧场、提醒、互动词包和资源库导入"
            : "输入新的激活码以更新此设备的绑定")
        subtitle.textColor = .secondaryLabelColor
        subtitle.maximumNumberOfLines = 2
        subtitle.widthAnchor.constraint(equalToConstant: 444).isActive = true
        stack.addArrangedSubview(subtitle)

        let inputLabel = NSTextField(labelWithString: "6 位激活码")
        inputLabel.font = .systemFont(ofSize: 13, weight: .semibold)
        inputLabel.setContentHuggingPriority(.required, for: .vertical)
        stack.setCustomSpacing(24, after: subtitle)
        stack.addArrangedSubview(inputLabel)
        input.placeholderString = "请输入激活码"
        input.font = .monospacedSystemFont(ofSize: 22, weight: .semibold)
        input.alignment = .center
        input.widthAnchor.constraint(equalToConstant: 444).isActive = true
        input.heightAnchor.constraint(equalToConstant: 42).isActive = true
        stack.addArrangedSubview(input)

        statusLabel.textColor = .systemRed
        statusLabel.font = .systemFont(ofSize: 12)
        statusLabel.stringValue = statusMessage ?? " "
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

        let website = NSButton(title: "去官网看看", target: self, action: #selector(openWebsite))
        let cancel = NSButton(title: required ? "继续使用免费版" : "取消", target: self, action: #selector(cancelPrompt))
        let submit = NSButton(title: "立即激活", target: self, action: #selector(submitPrompt))
        submit.keyEquivalent = "\r"
        submit.bezelColor = .controlAccentColor
        let buttons = NSStackView(views: [website, cancel, submit])
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
        contactBox.heightAnchor.constraint(equalToConstant: 250).isActive = true

        let qrView = NSImageView()
        qrView.imageScaling = .scaleProportionallyUpOrDown
        qrView.image = Self.contactImage()
        qrView.widthAnchor.constraint(equalToConstant: 200).isActive = true
        qrView.heightAnchor.constraint(equalToConstant: 200).isActive = true

        let title = NSTextField(labelWithString: "扫码添加作者微信")
        title.font = .systemFont(ofSize: 15, weight: .semibold)
        let note = wrappingLabel("备注「桌搭子」，支付 6.68 元后领取激活码。")
        let accountTitle = NSTextField(labelWithString: "微信号")
        accountTitle.font = .systemFont(ofSize: 11)
        accountTitle.textColor = .secondaryLabelColor
        let account = NSTextField(labelWithString: "wcl_lcw627")
        account.font = .monospacedSystemFont(ofSize: 14, weight: .semibold)
        account.isSelectable = true
        let support = wrappingLabel("这 6.68 元不是赎金，是给作者续一杯咖啡：赞助继续开发，桌搭子继续营业。想定制角色、动作或功能，也可以直接聊。")

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
        label.maximumNumberOfLines = 6
        label.lineBreakMode = .byWordWrapping
        return label
    }

    @objc private func toggleContact() {
        let show = contactBox.isHidden
        contactBox.isHidden = !show
        contactButton.title = show ? "收起联系方式  ‹" : "没有激活码？看看怎么支持作者  ›"
        window.setContentSize(NSSize(width: 500, height: show ? 665 : 395))
        window.center()
    }

    @objc private func submitPrompt() {
        let allowed = Set("ABCDEFGHJKLMNPQRSTUVWXYZ23456789")
        let normalized = input.stringValue.uppercased().filter { allowed.contains($0) }
        input.stringValue = String(normalized.prefix(6))
        guard input.stringValue.count == 6 else {
            statusLabel.stringValue = "请输入完整的 6 位激活码。"
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

    @objc private func openWebsite() {
        NSWorkspace.shared.open(URL(string: "https://desktoppet.online/")!)
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
