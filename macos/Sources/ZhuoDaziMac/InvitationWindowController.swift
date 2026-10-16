import AppKit

@MainActor
final class InvitationWindowController: NSObject, NSWindowDelegate {
    private let window: NSWindow
    private let input = NSTextField(string: "")
    private let statusLabel = NSTextField(labelWithString: "")
    private let contactButton = NSButton(title: "还没有激活码", target: nil, action: nil)
    private let contactBox = NSBox()
    private let wechatLabel = NSTextField(labelWithString: "wcl_lcw627")
    private let qrView = NSImageView()
    private let contactTitle = NSTextField(wrappingLabelWithString: "还没有激活码")
    private let contactDescription = NSTextView()
    private let remoteConfig = RemoteConfigService()
    private let licenses: LicenseService
    private let replacingExisting: Bool
    private var refreshTimer: Timer?
    private var refreshing = false
    private var finished = false
    private(set) var activatedRemotely = false
    private var result: String?

    init(licenses: LicenseService, replacingExisting: Bool = false, required: Bool, statusMessage: String? = nil, trialEnded: Bool = false) {
        self.licenses = licenses
        self.replacingExisting = replacingExisting
        window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 540, height: 760),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        super.init()
        window.title = trialEnded ? "完整体验结束，基础陪伴继续" : (required ? "继续完整体验" : "更换激活码")
        window.isReleasedWhenClosed = false
        window.delegate = self
        buildInterface(required: required, statusMessage: statusMessage, trialEnded: trialEnded)
        window.center()
    }

    func runModal() -> String? {
        result = nil
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        window.makeFirstResponder(input)
        refreshTimer = Timer(timeInterval: 15, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in await self?.refreshActivation() }
        }
        if let refreshTimer { RunLoop.main.add(refreshTimer, forMode: .modalPanel) }
        NSApp.runModal(for: window)
        finished = true
        refreshTimer?.invalidate()
        window.orderOut(nil)
        return result
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        result = nil
        NSApp.stopModal()
        return true
    }

    private func buildInterface(required: Bool, statusMessage: String?, trialEnded: Bool) {
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

        let heading = NSTextField(labelWithString: trialEnded ? "七天体验结束啦" : (required ? "继续完整体验" : "更换激活码"))
        heading.font = .systemFont(ofSize: 23, weight: .bold)
        stack.addArrangedSubview(heading)
        let subtitle = NSTextField(wrappingLabelWithString: trialEnded
            ? "桌搭子不会离开。刚才试过的玩法想接着用，填激活码就好；先留下桌宠也完全没问题。"
            : (required ? "一组码最多填两台。电脑激活后，手机再填同一组码也能进同一个账号。"
            : "输入新的激活码以更新此设备的绑定"))
        subtitle.textColor = .secondaryLabelColor
        subtitle.maximumNumberOfLines = 3
        subtitle.widthAnchor.constraint(equalToConstant: 484).isActive = true
        stack.addArrangedSubview(subtitle)

        let inputLabel = NSTextField(labelWithString: "6 位激活码（电脑和手机填同一组）")
        inputLabel.font = .systemFont(ofSize: 13, weight: .semibold)
        inputLabel.setContentHuggingPriority(.required, for: .vertical)
        stack.setCustomSpacing(24, after: subtitle)
        stack.addArrangedSubview(inputLabel)
        input.placeholderString = "请输入激活码"
        input.font = .monospacedSystemFont(ofSize: 22, weight: .semibold)
        input.alignment = .center
        input.widthAnchor.constraint(equalToConstant: 484).isActive = true
        input.heightAnchor.constraint(equalToConstant: 42).isActive = true
        stack.addArrangedSubview(input)

        let device = NSTextField(wrappingLabelWithString: "设备码：\(licenses.installationId)")
        device.font = .monospacedSystemFont(ofSize: 11, weight: .regular)
        device.isSelectable = true
        device.widthAnchor.constraint(equalToConstant: 484).isActive = true
        stack.addArrangedSubview(device)
        let copyDevice = NSButton(title: "复制设备码", target: self, action: #selector(copyDeviceCode))
        let sync = NSButton(title: "同步后台激活", target: self, action: #selector(syncActivation))
        sync.isEnabled = !replacingExisting
        stack.addArrangedSubview(NSStackView(views: [copyDevice, sync]))

        statusLabel.textColor = trialEnded ? .secondaryLabelColor : .systemRed
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
        applyRemoteContact()
        contactBox.isHidden = false
        contactButton.isHidden = true
        stack.addArrangedSubview(contactBox)
        Task { @MainActor [weak self] in
            await self?.refreshActivation()
        }

        let website = NSButton(title: trialEnded ? "去官网看看玩法" : "去官网看看", target: self, action: #selector(openWebsite))
        let cancel = NSButton(title: trialEnded ? "继续基础陪伴" : (required ? "先留下桌宠" : "取消"), target: self, action: #selector(cancelPrompt))
        let submit = NSButton(title: trialEnded ? "解锁完整功能" : "立即激活", target: self, action: #selector(submitPrompt))
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
            buttonContainer.widthAnchor.constraint(equalToConstant: 484),
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
        contactBox.widthAnchor.constraint(equalToConstant: 484).isActive = true
        contactBox.heightAnchor.constraint(equalToConstant: 264).isActive = true

        qrView.imageScaling = .scaleProportionallyUpOrDown
        qrView.image = Self.contactImage()
        qrView.widthAnchor.constraint(equalToConstant: 168).isActive = true
        qrView.heightAnchor.constraint(equalToConstant: 168).isActive = true

        contactTitle.font = .systemFont(ofSize: 15, weight: .semibold)
        let note = NSScrollView()
        note.hasVerticalScroller = true
        note.drawsBackground = false
        note.heightAnchor.constraint(equalToConstant: 92).isActive = true
        note.widthAnchor.constraint(equalToConstant: 226).isActive = true
        contactDescription.isEditable = false
        contactDescription.drawsBackground = false
        contactDescription.font = .systemFont(ofSize: 12)
        contactDescription.textColor = .secondaryLabelColor
        contactDescription.isVerticallyResizable = true
        contactDescription.autoresizingMask = [.width]
        contactDescription.textContainer?.widthTracksTextView = true
        note.documentView = contactDescription
        let accountTitle = NSTextField(labelWithString: "微信号")
        accountTitle.font = .systemFont(ofSize: 11)
        accountTitle.textColor = .secondaryLabelColor
        wechatLabel.font = .monospacedSystemFont(ofSize: 14, weight: .semibold)
        wechatLabel.isSelectable = true
        let copy = NSStackView(views: [contactTitle, note, accountTitle, wechatLabel])
        copy.orientation = .vertical
        copy.alignment = .leading
        copy.spacing = 7
        copy.widthAnchor.constraint(equalToConstant: 226).isActive = true
        let row = NSStackView(views: [qrView, copy])
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 16
        contactBox.contentView = row
    }

    private func refreshActivation() async {
        guard !refreshing, !finished else { return }
        refreshing = true
        defer { refreshing = false }
        await remoteConfig.refresh()
        guard !finished else { return }
        applyRemoteContact()
        if !replacingExisting {
            do {
                _ = try await licenses.checkTrial()
                guard !finished else { return }
                if licenses.isActivated {
                    activatedRemotely = true
                    NSApp.stopModal()
                    return
                }
            } catch { }
        }
    }

    private func applyRemoteContact() {
        let config = remoteConfig.current
        wechatLabel.stringValue = config.wechatId
        contactTitle.stringValue = replacingExisting ? config.activationReplacementTitle : config.activationTitle
        contactDescription.string = replacingExisting
            ? config.activationReplacementDescription : config.activationDescription
        contactDescription.setFrameSize(NSSize(width: 226, height: 92))
        contactDescription.sizeToFit()
        qrView.image = Self.contactImage()
        let image = config.activationQrCodeDataUrl
        if image.hasPrefix("data:image/png;base64,"), image.count <= 350_000,
           let data = Data(base64Encoded: String(image.dropFirst(22))), let decoded = NSImage(data: data) {
            qrView.image = decoded
        }
    }

    @objc private func copyDeviceCode() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(licenses.installationId, forType: .string)
        statusLabel.stringValue = "设备码已复制，可发给作者指定此设备激活。"
    }

    @objc private func syncActivation() {
        Task { @MainActor [weak self] in await self?.refreshActivation() }
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
        contactButton.title = show ? "收起联系方式  ‹" : "还没有激活码"
        window.setContentSize(NSSize(width: 540, height: show ? 760 : 480))
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
